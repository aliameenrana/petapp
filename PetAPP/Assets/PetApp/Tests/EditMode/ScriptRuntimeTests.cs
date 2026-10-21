using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PetApp.MiniGames;

namespace PetApp.Tests
{
    public class ScriptRuntimeTests
    {
        static readonly ScriptEvent[] Start = { new ScriptEvent { type = "start" } };
        static readonly ScriptEvent[] None = Array.Empty<ScriptEvent>();

        // Independent of the prelude's own allowlist, so a mistake there is caught here.
        static readonly HashSet<string> StandardGlobals = new HashSet<string>
        {
            "Object", "Function", "Array", "Number", "Boolean", "String", "Symbol", "BigInt", "Date", "Promise",
            "RegExp", "Error", "AggregateError", "EvalError", "RangeError", "ReferenceError", "SyntaxError",
            "TypeError", "URIError", "JSON", "Math", "Reflect", "Proxy", "Map", "Set", "WeakMap", "WeakSet",
            "WeakRef", "FinalizationRegistry", "ArrayBuffer", "SharedArrayBuffer", "DataView", "Atomics",
            "Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array", "Uint16Array", "Int32Array",
            "Uint32Array", "Float32Array", "Float64Array", "BigInt64Array", "BigUint64Array", "Iterator",
            "globalThis", "undefined", "NaN", "Infinity", "eval", "isFinite", "isNaN", "parseFloat", "parseInt",
            "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "escape", "unescape",
            "sdk", "onStart"
        };

        [Test]
        public void Time_counts_the_seconds_the_host_has_stepped()
        {
            using var runtime = new ScriptRuntime("function onStart() { sdk.timer.after(0.9, function () { sdk.log(sdk.time); }); }");

            runtime.Step(0, Start);
            runtime.Step(0.5f, None);
            var logs = runtime.Step(0.5f, None).Where(c => c.op == "log").Select(c => c.s).ToArray();

            Assert.AreEqual(new[] { "1" }, logs);
        }

        [Test]
        public void EndGame_reports_the_score_once()
        {
            using var runtime = new ScriptRuntime("function onStart() { sdk.endGame({ score: 42 }); sdk.endGame({ score: 99 }); }");

            var ends = runtime.Step(0, Start).Where(c => c.op == "end").ToArray();

            Assert.AreEqual(1, ends.Length);
            Assert.AreEqual(42, ends[0].n);
        }

        [Test]
        public void Script_cannot_reach_csharp_or_host_globals()
        {
            using var runtime = new ScriptRuntime(@"function onStart() {
                sdk.log(typeof CS, typeof puer, typeof require, typeof loadType, typeof console);
                sdk.log(Object.getOwnPropertyNames(globalThis).join(','));
            }");

            var logs = runtime.Step(0, Start).Where(c => c.op == "log").Select(c => c.s).ToArray();

            Assert.AreEqual("undefined undefined undefined undefined undefined", logs[0]);
            CollectionAssert.IsSubsetOf(logs[1].Split(','), StandardGlobals);
        }

        [Test]
        public void Runtime_error_surfaces_as_ScriptException()
        {
            using var runtime = new ScriptRuntime("function onStart() { throw new Error('boom'); }");

            var e = Assert.Throws<ScriptException>(() => runtime.Step(0, Start));
            StringAssert.Contains("boom", e.Message);
        }

        [Test]
        public void Syntax_error_fails_at_load()
        {
            Assert.Throws<ScriptException>(() => new ScriptRuntime("function onStart( {"));
        }

        [Test]
        public void Timer_fires_only_after_its_delay()
        {
            using var runtime = new ScriptRuntime("function onStart() { sdk.timer.after(1, function () { sdk.endGame({ score: 7 }); }); }");

            Assert.IsFalse(runtime.Step(0, Start).Any(c => c.op == "end"));
            Assert.IsFalse(runtime.Step(0.5f, None).Any(c => c.op == "end"));
            Assert.AreEqual(7, runtime.Step(0.6f, None).Single(c => c.op == "end").n);
        }

        [Test]
        public void Touch_event_reaches_the_spawned_objects_handler()
        {
            using var runtime = new ScriptRuntime(@"function onStart() {
                var prize = sdk.spawn('prize', { x: 1, y: 6 });
                prize.onTouch(sdk.pet, function () { sdk.endGame({ score: 1 }); });
            }");

            var spawn = runtime.Step(0, Start).Single(c => c.op == "spawn");
            Assert.AreEqual("prize", spawn.key);

            var touch = new[] { new ScriptEvent { type = "touch", id = spawn.id, other = 0 } };
            Assert.AreEqual(1, runtime.Step(0, touch).Single(c => c.op == "end").n);
        }
    }
}
