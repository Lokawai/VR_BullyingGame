using UnityEngine;
using UnityEngine.UI;

public class ButtonChangeLevel : MonoBehaviour
{
    [Header("References")]
    public Button targetButton; // The button that will change
    public Sprite[] imageList;  // Assign your Sprites here in the Inspector

    private int currentIndex = 0;

    public void ChangeToNextImage()
    {
        // 1. Safety check to ensure we have images to swap
        if (imageList == null || imageList.Length == 0) 
        {
            Debug.LogWarning("No sprites assigned to the list!");
            return;
        }

        // 2. Increment the index and wrap around using Modulo
        currentIndex = (currentIndex + 1) % imageList.Length;

        // 3. Directly assign the sprite (Much faster than Sprite.Create)
        targetButton.image.sprite = imageList[currentIndex];
    }
}
