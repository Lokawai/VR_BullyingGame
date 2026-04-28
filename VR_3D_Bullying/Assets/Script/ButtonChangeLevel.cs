using UnityEngine;
using UnityEngine.UI;

public class ButtonChangeLevel : MonoBehaviour
{
    public RawImage targetRawImage;
    public Texture[] textures;
    private int index = 0;

    public void NextTexture()
    {
        if (textures == null || textures.Length == 0) return;

        index = (index + 1) % textures.Length;
        targetRawImage.texture = textures[index];
    }
}
