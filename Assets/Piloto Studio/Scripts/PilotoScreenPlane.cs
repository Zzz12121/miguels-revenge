using UnityEngine;
namespace PilotoStudio
{
    // Compatibility replacement for the helper omitted from the imported archive.
    [DefaultExecutionOrder(11000)]
    public class PilotoScreenPlane : MonoBehaviour
    {
        public Camera targetCamera;
        public float overScaling=10;
        public bool useLocalZDistanceIfCameraParent=true;
        public bool planeUsesXZInsteadOfXY;
        void LateUpdate()
        {
            var cam=targetCamera?targetCamera:Camera.main;if(!cam)return;
            float distance=Mathf.Max(cam.nearClipPlane+.02f,Vector3.Dot(transform.position-cam.transform.position,cam.transform.forward));
            float height=cam.orthographic?cam.orthographicSize*2:2*distance*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f);
            float padding=1+Mathf.Max(0,overScaling)*.01f;
            transform.rotation=cam.transform.rotation*(planeUsesXZInsteadOfXY?Quaternion.Euler(90,0,0):Quaternion.identity);
            Vector3 size=planeUsesXZInsteadOfXY?new Vector3(height*cam.aspect*padding,1,height*padding):new Vector3(height*cam.aspect*padding,height*padding,1);
            var parent=transform.parent;var scale=parent?parent.lossyScale:Vector3.one;
            transform.localScale=new Vector3(size.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),size.y/Mathf.Max(.001f,Mathf.Abs(scale.y)),size.z/Mathf.Max(.001f,Mathf.Abs(scale.z)));
        }
    }
}
