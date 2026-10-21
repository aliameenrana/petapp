using System;
using System.Collections.Generic;
using System.Linq;
using Puerts;
using UnityEngine;

namespace PetApp.MiniGames
{
    [Serializable]
    public class ScriptCommand
    {
        public string op;
        public int id;
        public string key;
        public float x;
        public float y;
        public int n;
        public string s;
    }

    [Serializable]
    public class ScriptEvent
    {
        public string type;
        public int id;
        public int other;
        public float x;
        public float y;
    }

    public sealed class ScriptException : Exception
    {
        public ScriptException(string message, Exception inner) : base(message, inner) { }
    }

    // Game scripts never receive a C# object: the only channel is one JSON string in and one out per frame.
    // That keeps the sandbox closed (no reflection reachable from JS) and avoids IL2CPP stripping surprises.
    public sealed class ScriptRuntime : IDisposable
    {
        [Serializable]
        class FrameInput
        {
            public float dt;
            public int seed;
            public ScriptEvent[] events;
        }

        [Serializable]
        class CommandList
        {
            public ScriptCommand[] items;
        }

        JsEnv env;
        Func<string, string> frame;

        public ScriptRuntime(string gameCode)
            : this(Resources.Load<TextAsset>("PetApp/sdk-prelude.js").text, gameCode) { }

        public ScriptRuntime(string prelude, string gameCode)
        {
            env = new JsEnv();
            try
            {
                env.UsingFunc<string, string>();
                frame = env.Eval<Func<string, string>>(prelude, "sdk-prelude.js");
                env.Eval(gameCode, "game.js");
            }
            catch (Exception e)
            {
                Dispose();
                throw new ScriptException(e.Message, e);
            }
        }

        public ScriptCommand[] Step(float dt, IReadOnlyList<ScriptEvent> events, int seed = 0)
        {
            var input = JsonUtility.ToJson(new FrameInput { dt = dt, seed = seed, events = events.ToArray() });
            string output;
            try
            {
                output = frame(input);
            }
            catch (Exception e)
            {
                throw new ScriptException(e.Message, e);
            }
            return JsonUtility.FromJson<CommandList>(output).items ?? Array.Empty<ScriptCommand>();
        }

        public void Dispose()
        {
            frame = null;
            env?.Dispose();
            env = null;
        }
    }
}
