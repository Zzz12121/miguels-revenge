using UnityEngine;
namespace PilotoStudio
{
    [DefaultExecutionOrder(10999)]
    public class PilotoCameraOffset : MonoBehaviour
    {
        public Vector3 offset=new Vector3(0,0,.5f);
        void LateUpdate(){var cam=Camera.main;if(cam)transform.position=cam.transform.TransformPoint(offset);}
    }
}
