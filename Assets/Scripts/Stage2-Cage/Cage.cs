using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Cage : MonoBehaviour
{
    public string correctCreatureTag;
    public TextMeshProUGUI label;
    public GameObject door;
    public Transform snapPoint;
    public AudioClip victorySound;
    public int totalCages = 4;
    public Vector3 doorCloseRotation = new Vector3(0, 90, 0);
    public float doorCloseDuration = 0.5f;

    private GameObject capturedCreature;
    private bool isLocked = false;

    private static int correctCount = 0;
    private static bool resetDone = false;

    void Awake()
    {
        if (!resetDone)
        {
            correctCount = 0;
            resetDone = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isLocked) return;
        if (!other.transform.root.CompareTag(correctCreatureTag)) return;

        capturedCreature = other.transform.root.gameObject;

        if (snapPoint != null)
        {
            capturedCreature.transform.position = snapPoint.position;
            capturedCreature.transform.rotation = snapPoint.rotation;
        }
        else
        {
            capturedCreature.transform.position = transform.position;
        }

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        Animator anim = capturedCreature.GetComponent<Animator>();
        if (anim != null) anim.SetTrigger("Idle");

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetCaged();

        SlimeCreature slime = capturedCreature.GetComponent<SlimeCreature>();
        if (slime != null) slime.SetCaged();

        ScorpionCreature scorpion = capturedCreature.GetComponent<ScorpionCreature>();
        if (scorpion != null) scorpion.SetCaged();

        BoxerCreature boxer = capturedCreature.GetComponent<BoxerCreature>();
        if (boxer != null) boxer.SetCaged();

        label.color = Color.green;
        isLocked = true;
        correctCount++;
        if (correctCount >= totalCages && victorySound != null)
            AudioSource.PlayClipAtPoint(victorySound, Camera.main.transform.position);
        if (door != null) StartCoroutine(CloseDoor());
    }

    void OnTriggerExit(Collider other)
    {
        if (isLocked) return;
        if (capturedCreature == null) return;
        if (other.transform.root.gameObject != capturedCreature) return;

        label.color = Color.white;

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = true;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetFree();

        SlimeCreature slime = capturedCreature.GetComponent<SlimeCreature>();
        if (slime != null) slime.SetFree();

        ScorpionCreature scorpion = capturedCreature.GetComponent<ScorpionCreature>();
        if (scorpion != null) scorpion.SetFree();

        BoxerCreature boxer = capturedCreature.GetComponent<BoxerCreature>();
        if (boxer != null) boxer.SetFree();

        capturedCreature = null;
    }

    System.Collections.IEnumerator CloseDoor()
    {
        float elapsed = 0f;
        Quaternion startRot = door.transform.localRotation;
        Quaternion endRot = Quaternion.Euler(door.transform.localEulerAngles + doorCloseRotation);

        while (elapsed < doorCloseDuration)
        {
            elapsed += Time.deltaTime;
            door.transform.localRotation = Quaternion.Lerp(startRot, endRot, elapsed / doorCloseDuration);
            yield return null;
        }
        door.transform.localRotation = endRot;
    }
        void OnDisable()
    {
        correctCount = 0;
        resetDone = false;
    }
}