using UnityEngine;

/// <summary>Quiet river and intermittent existing birds, without dialogue or combat cues.</summary>
public sealed class InkboundAmbience : MonoBehaviour
{
    public AudioClip riverLoop;
    public AudioClip[] birds;
    public Vector2 birdInterval = new Vector2(9,18);
    public float birdVolume = .1f;
    AudioSource river,bird;
    float next;
    void Start()
    {
        if(riverLoop)
        {
            river=gameObject.AddComponent<AudioSource>();river.clip=riverLoop;river.loop=true;
            river.spatialBlend=1;river.minDistance=6;river.maxDistance=55;river.volume=.14f;river.Play();
        }
        bird=gameObject.AddComponent<AudioSource>();bird.spatialBlend=1;bird.minDistance=8;bird.maxDistance=90;
        next=Time.time+Random.Range(2,6);
    }
    void Update()
    {
        if(Time.time<next || birds==null || birds.Length==0)return;
        next=Time.time+Random.Range(birdInterval.x,birdInterval.y);
        bird.pitch=Random.Range(.92f,1.06f);bird.PlayOneShot(birds[Random.Range(0,birds.Length)],birdVolume);
    }
}
