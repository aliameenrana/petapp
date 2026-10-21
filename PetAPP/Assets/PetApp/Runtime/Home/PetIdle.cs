using System.Linq;
using UnityEngine;

namespace PetApp.Home
{
    // The pet model ships rigged but without clips, so idle life is driven on the bones directly.
    public sealed class PetIdle : MonoBehaviour
    {
        sealed class Bone
        {
            public Transform t;
            public Quaternion rest;
        }

        Bone head, neck, tail, leftEar, rightEar;
        Transform spine;
        Vector3 spineRestScale;
        float excite;
        float nextTwitch;
        float twitch;

        void Start()
        {
            head = Find("Head");
            neck = Find("Neck");
            tail = Find("Tail");
            leftEar = Find("LeftEar");
            rightEar = Find("RightEar");
            spine = Find("Spine")?.t;
            if (spine != null) spineRestScale = spine.localScale;
        }

        Bone Find(string suffix)
        {
            var t = GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name.EndsWith("_" + suffix));
            return t == null ? null : new Bone { t = t, rest = t.localRotation };
        }

        public void Excite() => excite = 1f;

        void LateUpdate()
        {
            var time = Time.time;
            excite = Mathf.MoveTowards(excite, 0f, Time.deltaTime * 0.7f);
            var energy = 1f + excite * 2f;

            if (time > nextTwitch)
            {
                twitch = 1f;
                nextTwitch = time + Random.Range(1.5f, 4f);
            }
            twitch = Mathf.MoveTowards(twitch, 0f, Time.deltaTime * 6f);

            Rotate(head, Mathf.Sin(time * 1.6f) * 4f * energy, Mathf.Sin(time * 0.7f) * 8f, Mathf.Sin(time * 1.1f) * 3f * energy);
            Rotate(neck, Mathf.Sin(time * 1.6f + 0.5f) * 2f * energy, 0f, 0f);
            Rotate(tail, 0f, Mathf.Sin(time * (5f + excite * 12f)) * (12f + excite * 18f), 0f);
            var ear = Mathf.Sin(time * 2.5f) * 4f * energy + twitch * 15f;
            Rotate(leftEar, ear, 0f, 0f);
            Rotate(rightEar, -ear, 0f, 0f);

            if (spine != null) spine.localScale = spineRestScale * (1f + Mathf.Sin(time * 2.2f) * 0.025f);
        }

        static void Rotate(Bone bone, float x, float y, float z)
        {
            if (bone != null) bone.t.localRotation = bone.rest * Quaternion.Euler(x, y, z);
        }
    }
}
