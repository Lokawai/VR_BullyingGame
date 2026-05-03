using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class ChangeLevelDifferentButton : MonoBehaviour
{
    [Header("References")]
    public Image levelImage;
    public TMP_Text levelNameText;
    public Button prevButton;
    public Button nextButton;
    public Button startButton;

    [Header("Data")]
    public LevelEntry[] levelEntries;  // Array of level entries    

    private int currentIndex = 0;

    void Start()
    {
        DisplayLevelInfo();
    }

    private void OnEnable()
    {
        prevButton.onClick.AddListener(PreviousLevel);
        nextButton.onClick.AddListener(NextLevel);
        startButton.onClick.AddListener(EnterScreen);
    }   

    private void OnDisable()
    {
        prevButton.onClick.RemoveListener(PreviousLevel);
        nextButton.onClick.RemoveListener(NextLevel);
        startButton.onClick.RemoveListener(EnterScreen);
    }

    // Call this from the "Next" Button
    public void PreviousLevel()
    {
        currentIndex--;
        // Loop back to the last index if we go below 0
        if (currentIndex < 0)
            currentIndex = levelEntries.Length - 1;

        DisplayLevelInfo();
    }

    // Call this from the "Next" Button
    public void NextLevel()
    {
        currentIndex++;
        // Loop back to 0 if we exceed the array length
        if (currentIndex >= levelEntries.Length)
            currentIndex = 0;

        DisplayLevelInfo();
    }

    // Call this from the "Action" Button (the one with the sprite)
    public void EnterScreen()
    {
        string sceneToLoad = levelEntries[currentIndex].sceneName;
        Debug.Log("Loading: " + sceneToLoad);
        SceneManager.LoadScene(sceneToLoad);
    }

    private void DisplayLevelInfo()
    {
        if (levelEntries == null || levelEntries.Length == 0)
        {
            Debug.LogWarning("No level entries assigned!");
            return;
        }

        LevelEntry currentEntry = levelEntries[currentIndex];
        levelImage.sprite = currentEntry.sprite;
        levelNameText.text = currentEntry.sceneName; // You can customize this to show a nicer name
    }

    [System.Serializable]
    public struct LevelEntry
    {
        public Sprite sprite;
        public string sceneName;
    }
}
