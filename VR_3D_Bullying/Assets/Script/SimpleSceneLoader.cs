using UnityEngine;
using UnityEngine.SceneManagement;

public class SimpleSceneLoader : MonoBehaviour
{
    [SerializeField] private string sceneName;

    public void LoadScene()
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("Scene name is not set!");
        }
    }
}
