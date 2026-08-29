using UnityEngine;
using UnityEngine.UI;

public class ShowInformationBoard : MonoBehaviour
{
    [Header("Image Settings")]
    [SerializeField] private Image[] images;
    
    [Header("Button Settings")]
    [SerializeField] private Button[] nextButtons;
    
    private int currentIndex = 0;
    
    private void Start()
    {
        InitializeBoard();
    }
    
    private void InitializeBoard()
    {
        if (images == null || images.Length == 0 || nextButtons == null || nextButtons.Length == 0)
        {
            Debug.LogError("Images or Buttons not assigned!");
            return;
        }
        
        for (int i = 0; i < images.Length; i++)
        {
            images[i].gameObject.SetActive(i == 0);
            
            int buttonIndex = i;
            nextButtons[i].onClick.AddListener(() => ShowNextImage(buttonIndex));
        }
    }
    
    private void ShowNextImage(int currentButtonIndex)
    {
        images[currentButtonIndex].gameObject.SetActive(false);
        
        currentIndex = (currentButtonIndex + 1) % images.Length;
        
        images[currentIndex].gameObject.SetActive(true);
    }
    
    public void SetImages(Image[] newImages)
    {
        images = newImages;
        InitializeBoard();
    }
    
    public void SetButtons(Button[] newButtons)
    {
        nextButtons = newButtons;
        InitializeBoard();
    }
}
