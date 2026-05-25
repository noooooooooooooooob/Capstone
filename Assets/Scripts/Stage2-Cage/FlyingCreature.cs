using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class FlyingCreature : MonoBehaviour
{
    public enum State { Flying, NetCaught, Grabbed, Caged }
    public State currentState = State.Flying;
    public Transform[] waypoints;
    public float speed = 2f;
    private int currentTarget = 0;
    private int direction = 1;
    private XRGrabInteractable grab;
    private Animator anim;
    private Transform caughtInNet;
    public HintPaper hintPaper;
    public AudioClip grabSound;
    private AudioSource audioSource;

    void Start()
    {
        grab = GetComponent<XRGrabInteractable>();
        anim = GetComponentInChildren<Animator>();
        if (grab != null)
        {
            grab.enabled = false;
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = grabSound;
        audioSource.loop = true;
        audioSource.playOnAwake = false;

        // lock hint paper on start
        if (hintPaper != null)
        {
            Rigidbody rb = hintPaper.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            XRGrabInteractable paperGrab = hintPaper.GetComponent<XRGrabInteractable>();
            if (paperGrab != null) paperGrab.enabled = false;
        }
    }

    void Update()
    {
        switch (currentState)
        {
            case State.Flying:
                UpdateFlying();
                break;
            case State.NetCaught:
                if (caughtInNet != null)
                    transform.position = caughtInNet.position;
                break;
            case State.Grabbed:
            case State.Caged:
                break;
        }
    }

    void UpdateFlying()
    {
        Transform target = waypoints[currentTarget];
        float hover = Mathf.Sin(Time.time * 2f) * 0.3f;
        Vector3 targetPos = target.position + new Vector3(0, hover, 0);
        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
        Vector3 dir = targetPos - transform.position;
        if (dir.magnitude > 0.2f)
            transform.rotation = Quaternion.LookRotation(dir);
        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            currentTarget += direction;
            if (currentTarget >= waypoints.Length)
            {
                currentTarget = waypoints.Length - 2;
                direction = -1;
            }
            else if (currentTarget < 0)
            {
                currentTarget = 1;
                direction = 1;
            }
        }
    }

    public void SetNetCaught(Transform netCenter)
    {
        currentState = State.NetCaught;
        caughtInNet = netCenter;
        if (grab != null) grab.enabled = true;
        if (anim != null) anim.SetTrigger("Struggle");
        if (hintPaper != null) hintPaper.Drop();
    }

    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        currentState = State.Grabbed;
        caughtInNet = null;
        if (anim != null) anim.SetTrigger("Stunned");
        if (audioSource != null && grabSound != null) audioSource.Play();
    }

    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (audioSource != null) audioSource.Stop();
    }

    public void SetCaged()
    {
        currentState = State.Caged;
        if (grab != null) grab.enabled = false;
        if (anim != null) anim.SetTrigger("Idle");
        if (audioSource != null) audioSource.Stop();
    }

    public void SetFree()
    {
        currentState = State.Flying;
        caughtInNet = null;
        if (grab != null) grab.enabled = false;
        if (audioSource != null) audioSource.Stop();
    }
}