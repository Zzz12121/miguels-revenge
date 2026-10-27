using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PilotoStudio
{
    public class ParticleHandler : MonoBehaviour
    {
        public GameObject castParticle;
        public float castFXDuration;
        public GameObject loopingParticle;
        public float loopDuration;
        public GameObject endParticle;

        private ParticleSystem castParticleSystem;
        private ParticleSystem loopingParticleSystem;
        private ParticleSystem endParticleSystem;

        private void OnEnable()
        {
            if(!castParticle || !loopingParticle || !endParticle)return;
            castParticleSystem = castParticle.GetComponent<ParticleSystem>();
            loopingParticleSystem = loopingParticle.GetComponent<ParticleSystem>();
            endParticleSystem = endParticle.GetComponent<ParticleSystem>();
            if (!castParticleSystem || !loopingParticleSystem || !endParticleSystem)
            {
                Debug.LogError("ParticleHandler: Missing particle systems. Ensure they are referenced correctly.");
                return;
            }

            Cast();
        }

        public void Cast()
        {
            if(!Application.isPlaying || !isActiveAndEnabled || !castParticleSystem || !loopingParticleSystem || !endParticleSystem)return;
            StopAllCoroutines();
            foreach(var ps in GetComponentsInChildren<ParticleSystem>(true))ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            StartCoroutine(Flow());
        }
        void OnDisable(){StopAllCoroutines();}

        IEnumerator Flow()
        {
            PlayParticles(castParticleSystem, castFXDuration);
            yield return new WaitForSeconds(castFXDuration);

            PlayParticles(loopingParticleSystem, loopDuration);
            yield return new WaitForSeconds(loopDuration);

            PlayParticles(endParticleSystem);
            yield return WaitUntilParticleSystemStops(endParticleSystem);
        }
        private IEnumerator WaitUntilParticleSystemStops(ParticleSystem particleSystem)
        {
            while (particleSystem.IsAlive(true))
            {
                yield return null;
            }
        }
        private void PlayParticles(ParticleSystem particleSystem, float duration = 0f)
        {
            particleSystem.gameObject.SetActive(true);
            if (particleSystem.main.startLifetime.constantMax == Mathf.Infinity)
                StartCoroutine(WaitUntilParticleSystemStops(particleSystem));

            particleSystem.Play();

            if (duration > 0f && particleSystem.main.startLifetime.constantMax != Mathf.Infinity)
            {
                StartCoroutine(StopParticleAfterTime(particleSystem, duration));
            }
        }

        IEnumerator StopParticleAfterTime(ParticleSystem particleSystem, float duration)
        {
            yield return new WaitForSeconds(duration);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            //   particleSystem.gameObject.SetActive(false);
        }
    }




}
