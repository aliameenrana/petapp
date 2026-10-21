using DG.Tweening;
using UnityEngine;

namespace PetApp.Home
{
    public sealed class PetJuice : MonoBehaviour
    {
        const float ComboWindow = 0.45f;
        const int SpinEvery = 5;

        [SerializeField] Transform body;
        [SerializeField] Animator animator;
        [SerializeField] ParticleSystem burst;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip jumpClip;
        [SerializeField] AudioClip landClip;
        [SerializeField] AudioClip comboClip;
        [SerializeField] Camera cam;

        int combo;
        float lastTap = -10f;
        Sequence sequence;
        Vector3 basePosition;
        Vector3 baseScale;
        Quaternion baseRotation;
        float baseFov;

        void Awake()
        {
            basePosition = transform.localPosition;
            baseScale = body.localScale;
            baseRotation = transform.localRotation;
            baseFov = cam.fieldOfView;
        }

        Vector3 Squash(float x, float y, float z) => Vector3.Scale(baseScale, new Vector3(x, y, z));

        public void Tap()
        {
            combo = Time.time - lastTap < ComboWindow ? combo + 1 : 1;
            lastTap = Time.time;
            var spin = combo % SpinEvery == 0;
            var height = 0.6f + Mathf.Min(combo, SpinEvery) * 0.15f;

            // Kill and reset first, or rapid taps leave the scale and rotation drifting.
            sequence?.Kill();
            body.DOKill();
            transform.DOKill();
            body.localScale = baseScale;
            transform.localRotation = baseRotation;

            if (animator != null) animator.SetTrigger("Jump");
            if (TryGetComponent<PetIdle>(out var idle)) idle.Excite();
            audioSource.pitch = 1f + Mathf.Min(combo, SpinEvery) * 0.06f + Random.Range(-0.04f, 0.04f);
            audioSource.PlayOneShot(jumpClip);

            sequence = DOTween.Sequence()
                .Append(body.DOScale(Squash(1.25f, 0.75f, 1.25f), 0.07f))
                .Append(body.DOScale(Squash(0.85f, 1.2f, 0.85f), 0.12f))
                .Join(transform.DOLocalMoveY(basePosition.y + height, 0.25f).SetEase(Ease.OutQuad))
                .Append(body.DOScale(Squash(1f, 1f, 1f), 0.12f))
                .Append(transform.DOLocalMoveY(basePosition.y, 0.22f).SetEase(Ease.InQuad))
                .AppendCallback(Land)
                .Append(body.DOScale(Squash(1.3f, 0.7f, 1.3f), 0.06f))
                .Append(body.DOScale(Squash(1f, 1f, 1f), 0.4f).SetEase(Ease.OutElastic))
                .InsertCallback(0.3f, () => Burst(spin));
            if (spin)
                sequence.Insert(0.07f, transform.DOLocalRotate(new Vector3(0f, 360f, 0f), 0.45f, RotateMode.LocalAxisAdd).SetEase(Ease.OutCubic));

            cam.DOKill();
            cam.fieldOfView = baseFov;
            cam.DOFieldOfView(baseFov * 0.96f, 0.08f).SetLoops(2, LoopType.Yoyo);
        }

        void Land()
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(landClip);
        }

        void Burst(bool spin)
        {
            burst.Emit(spin ? 40 : 12);
            if (spin) audioSource.PlayOneShot(comboClip);
        }
    }
}
