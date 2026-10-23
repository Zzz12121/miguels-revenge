using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PilotoStudio
{
    public class ParticleShowcase : MonoBehaviour
    {
        [SerializeField]
        private List<GameObject> particles = new List<GameObject>();
        [SerializeField]
        private int currentlyActive = 0;
        [SerializeField]
        private Text displayName;
        public int ActiveIndex=>currentlyActive;
        public int EffectCount=>particles.Count;

        private void Start()
        {
            foreach (Transform t in this.transform)
            {
                if(!particles.Contains(t.gameObject))particles.Add(t.gameObject);
            }
            particles.RemoveAll(p=>!p);
            if(particles.Count==0)return;
            currentlyActive=Mathf.Clamp(currentlyActive,0,particles.Count-1);
            foreach(var effect in particles)effect.SetActive(false);
            particles[currentlyActive].SetActive(true);
            PostUpdateLogic();
        }

        void PostUpdateLogic()
        {
            if(particles.Count==0)return;
            if (displayName != null)
            displayName.text = particles[currentlyActive].name;
            if (particles[currentlyActive].TryGetComponent<ParticleHandler>(out ParticleHandler handler))
            {
                handler.Cast();
            }
        }


        public void ActivateNext()
        {
            if(particles.Count==0)return;
            if (currentlyActive + 1 >= particles.Count)
            {
                particles[currentlyActive].SetActive(false);
                currentlyActive = 0;
                particles[currentlyActive].SetActive(true);
            }
            else
            {
                particles[currentlyActive].SetActive(false);
                currentlyActive++;
                particles[currentlyActive].SetActive(true);
            }

            PostUpdateLogic();
        }

        public void ActivatePrevious()
        {
            if(particles.Count==0)return;
            if (currentlyActive - 1 < 0)
            {
                particles[currentlyActive].SetActive(false);
                currentlyActive = (particles.Count - 1);
                particles[currentlyActive].SetActive(true);
            }
            else
            {
                particles[currentlyActive].SetActive(false);
                currentlyActive--;
                particles[currentlyActive].SetActive(true);
            }

            PostUpdateLogic();
        }


        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb=Keyboard.current;if(kb==null)return;
            bool previous=kb.qKey.wasPressedThisFrame;
            bool next=kb.wKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool replay=kb.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            bool previous=Input.GetKeyDown(KeyCode.Q);
            bool next=Input.GetKeyDown(KeyCode.W)||Input.GetKeyDown(KeyCode.D);
            bool replay=Input.GetKeyDown(KeyCode.Space);
#else
            bool previous=false,next=false,replay=false;
#endif
            if (previous)
            {
                ActivatePrevious();
            }

            if (next)
            {
                ActivateNext();
            }

            if (replay && particles.Count>0)
            {
          
                foreach(var ps in particles[currentlyActive].GetComponentsInChildren<ParticleSystem>(true))ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach(var ps in particles[currentlyActive].GetComponentsInChildren<ParticleSystem>())ps.Play(false);
                PostUpdateLogic() ;
            }
        }

    }
}
