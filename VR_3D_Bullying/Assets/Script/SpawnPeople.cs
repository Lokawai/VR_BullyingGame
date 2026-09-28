using UnityEngine;

public class SpawnPeople : MonoBehaviour
{
   [Header("生成設定")]
    public GameObject[] personPrefab; // 人物 Prefab
    public Transform[] spawnPoints; // 生成點位置列表
    public int spawnCount = 5;      // 生成數量

    void Start()
    {
        SpawnP();
    }

    public void SpawnP()
    {
        if (personPrefab == null || spawnPoints.Length == 0) return;

        for (int i = 0; i < spawnCount; i++)
        {
            // 隨機選擇一個生成點
            Transform randomPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

            // 生成人物並指定位置與旋轉
            Instantiate(personPrefab[Random.Range(0, personPrefab.Length)], randomPoint.position, randomPoint.rotation);
        }
    }
}
