using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class NPCSpawner : MonoBehaviour
{
    [Header("完整 NPC Prefabs")]
    public GameObject[] npcPrefabs;

    [Header("Spawn Points")]
    public Transform[] spawnPoints;

    [Header("Spawn Settings")]
    [Min(1)] public int spawnAmount = 10;
    public bool spawnOnStart = true;

    [Tooltip("每隔幾多秒 Spawn 一隻 NPC")]
    public float spawnInterval = 2f;

    [Tooltip("開始遊戲後，等幾多秒先 Spawn 第一隻")]
    public float firstSpawnDelay = 1f;

    [Header("Avoid Spawn Overlap")]
    public float spawnCheckRadius = 0.8f;
    public LayerMask npcLayer;

    void Start()
    {
        if (spawnOnStart)
        {
            StartCoroutine(SpawnNPCsOverTime());
        }
    }

    IEnumerator SpawnNPCsOverTime()
    {
        if (npcPrefabs == null || npcPrefabs.Length == 0)
        {
            Debug.LogWarning("冇放任何 NPC Prefab 入 Npc Prefabs。");
            yield break;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("冇放任何 Spawn Point 入 Spawn Points。");
            yield break;
        }

        // 遊戲開始後，先等一等
        yield return new WaitForSeconds(firstSpawnDelay);

        int spawnedCount = 0;
        int maxAttempts = spawnAmount * 10;

        while (spawnedCount < spawnAmount && maxAttempts > 0)
        {
            maxAttempts--;

            Transform point = spawnPoints[
                Random.Range(0, spawnPoints.Length)
            ];

            // 如果設定咗 NPC Layer，而且該位置已經有 NPC，就唔喺呢度 Spawn
            bool positionOccupied = false;

            if (npcLayer.value != 0)
            {
                positionOccupied = Physics.CheckSphere(
                    point.position,
                    spawnCheckRadius,
                    npcLayer
                );
            }

            if (!positionOccupied)
            {
                GameObject npcPrefab = npcPrefabs[
                    Random.Range(0, npcPrefabs.Length)
                ];

                GameObject spawnedNPC = Instantiate(
                    npcPrefab,
                    point.position,
                    point.rotation
                );

                NavMeshAgent agent =
                    spawnedNPC.GetComponent<NavMeshAgent>();

                if (agent == null)
                {
                    Debug.LogWarning(
                        spawnedNPC.name +
                        " 未有 NavMeshAgent，所以唔會行路。"
                    );
                }
                else if (!agent.isOnNavMesh)
                {
                    Debug.LogWarning(
                        spawnedNPC.name +
                        " 冇 Spawn 喺 NavMesh 上。請檢查 Spawn Point 位置。"
                    );
                }

                spawnedCount++;

                Debug.Log(
                    "Spawn NPC " +
                    spawnedCount +
                    " / " +
                    spawnAmount
                );

                // 成功 Spawn 一隻後，先等指定時間，然後下一隻
                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // 呢個 Spawn Point 有人，等少少再試另一個點
                yield return new WaitForSeconds(0.2f);
            }
        }

        Debug.Log("完成 Spawn NPC： " + spawnedCount + " 隻。");
    }
}