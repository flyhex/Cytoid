using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

public class GameProgressIndicator : MonoBehaviour
{
    [GetComponent] public ProceduralImage image;

    public Game game;
    
    private float fullWidth;
    
    private void Awake()
    {
        fullWidth = GetComponentInParent<CanvasScaler>().referenceResolution.x;
        image.rectTransform.SetWidth(0);
        game.onGameUpdate.AddListener(OnGameUpdate);
    }

    private void OnGameUpdate(Game game)
    {
        if (game.State.UseHealthSystem)
        {
            image.rectTransform.DOWidth((float) (fullWidth * game.State.Health / game.State.MaxHealth), 0.2f);
        }
        else
        {
            image.rectTransform.DOWidth(fullWidth * game.ChartProgress, 0.2f);
        }
    }
    
}