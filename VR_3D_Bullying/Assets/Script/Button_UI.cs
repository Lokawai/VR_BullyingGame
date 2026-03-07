using UnityEngine;
using UnityEngine.UIElements;

public class Button_UI : MonoBehaviour
{
   public Renderer rend;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        Button button = root.Q<Button>("Start");
    }

   public void LoadScreen(string sceneName)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
    }
}
