using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScreenManager : SingletonMonoBehavior<ScreenManager>
{
    public Canvas rootCanvas;

    public List<Screen> screenPrefabs;
    public string initialScreenId;

    public List<Screen> createdScreens;

    public Screen ActiveScreen => createdScreens.Find(it => it.GetId() == ActiveScreenId);
    public Stack<Intent> History { get; set; } = new Stack<Intent>();

    public string ActiveScreenId { get; protected set; }
    public string ChangingToScreenId { get; protected set; }
    public bool IsChangingScreen => ChangingToScreenId != null;
    private HashSet<ScreenChangeListener> screenChangeListeners = new HashSet<ScreenChangeListener>();
    private CancellationTokenSource screenChangeCancellationTokenSource;

    protected override void Awake()
    {
        base.Awake();
        Context.ScreenManager = this;

#if UNITY_EDITOR
        if (false)
        {
            foreach (var screen in createdScreens)
            {
                var localPath = "Assets/Resources/Prefabs/Screens/" + screen.GetId() + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(screen.gameObject, localPath,
                    InteractionMode.AutomatedAction);
                screenPrefabs.Add(prefab.GetComponent<Screen>());
                Destroy(screen.gameObject);
            }

            createdScreens.Clear();
        }
#endif
    }

    private void Start()
    {
        createdScreens.ForEach(it => it.gameObject.SetActive(false));
        if (!string.IsNullOrEmpty(initialScreenId))
        {
            ChangeScreen(initialScreenId, ScreenTransition.None);
        }
    }

    public bool IsScreenCreated(string id)
    {
        return createdScreens.Any(it => it.GetId() == id);
    }

    public Screen GetScreen(string id)
    {
        return createdScreens.Find(it => it.GetId() == id);
    }

    public T GetScreen<T>() where T : Screen
    {
        return (T) createdScreens.Find(it => it.GetType() == typeof(T));
    }
    
    public Intent PeekHistory()
    {
        return History.Count > 1 ? History.Peek() : null;
    }

    public Intent PopAndPeekHistory()
    {
        return History.Count > 1 ? History.Also(it => it.Pop()).Peek() : null;
    }

    public Screen CreateScreen(string id)
    {
        var prefab = screenPrefabs.Find(it => it.GetId() == id);
        if (prefab == null)
        {
            throw new ArgumentException($"Screen {id} does not exist");
        }

        var gameObject = Instantiate(prefab.gameObject, rootCanvas.transform);

        var newScreen = gameObject.GetComponent<Screen>();
        newScreen.gameObject.SetActive(true);
        createdScreens.Add(newScreen);
        return newScreen;
    }

    public void DestroyScreen(string id)
    {
        var screen = createdScreens.Find(it => it.GetId() == id);
        if (screen != null)
        {
            screen.State = ScreenState.Destroyed;
            Destroy(screen.gameObject);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        screenChangeCancellationTokenSource?.Cancel();
    }

    public void ChangeScreen(
        Intent intent,
        ScreenTransition transition,
        float duration = 0.4f,
        float currentScreenTransitionDelay = 0f,
        float newScreenTransitionDelay = 0f,
        Vector2? transitionFocus = null,
        Action<Screen> onFinished = null,
        bool willDestroy = false,
        bool addTargetScreenToHistory = true
    )
    { 
        ChangeScreen(intent.ScreenId, transition, duration, currentScreenTransitionDelay, newScreenTransitionDelay,
            transitionFocus, onFinished, willDestroy, addTargetScreenToHistory, intent.Payload);
    }

    public async void ChangeScreen(
        string targetScreenId,
        ScreenTransition transition,
        float duration = 0.4f,
        float currentScreenTransitionDelay = 0f,
        float newScreenTransitionDelay = 0f,
        Vector2? transitionFocus = null,
        Action<Screen> onFinished = null,
        bool willDestroy = false,
        bool addTargetScreenToHistory = true,
        ScreenPayload payload = default
    )
    {
        if (ChangingToScreenId != null)
        {
            print($"Warning: Already changing to {ChangingToScreenId}! Ignoring.");
            return;
        }

        if (ActiveScreen != null && targetScreenId == ActiveScreen.GetId())
        {
            transition = ScreenTransition.None;
            newScreenTransitionDelay = duration / 2f;
        }

        if (ChangingToScreenId == targetScreenId)
        {
            print("Warning: Already changing to the same screen! Ignoring.");
            return;
        }
        ChangingToScreenId = targetScreenId;
        print($"Changing screen to {targetScreenId}");

        if (transition == ScreenTransition.None)
        {
            duration = 0;
        }

        var lastScreen = ActiveScreen;
        var newScreen = createdScreens.Find(it => it.GetId() == targetScreenId);

        var newScreenWasCreated = false;
        if (newScreen == null)
        {
            newScreen = CreateScreen(targetScreenId);
            newScreenWasCreated = true;
        }
        
        var disableTransitions = Context.ShouldDisableMenuTransitions();

        if (lastScreen != null)
        {
            if (disableTransitions)
            {
                OpaqueOverlay.Show(duration);
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(duration),
                        cancellationToken: (screenChangeCancellationTokenSource = new CancellationTokenSource()).Token);
                }
                catch
                {
                    ChangingToScreenId = null;
                    return;
                }
                lastScreen.CanvasGroup.alpha = 0;
            }
            lastScreen.State = ScreenState.Inactive;

            if (currentScreenTransitionDelay > 0)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(currentScreenTransitionDelay),
                        cancellationToken: (screenChangeCancellationTokenSource = new CancellationTokenSource()).Token);
                }
                catch
                {
                    ChangingToScreenId = null;
                    return;
                }
            }
            
            if (!disableTransitions)
            {
                lastScreen.CanvasGroup.DOFade(0, duration);

                if (transition != ScreenTransition.None)
                {
                    switch (transition)
                    {
                        case ScreenTransition.In:
                            if (transitionFocus.HasValue && transitionFocus != Vector2.zero)
                            {
                                var difference =
                                    new Vector2(Context.ReferenceWidth / 2f, Context.ReferenceHeight / 2f) -
                                    transitionFocus.Value;
                                lastScreen.RectTransform.DOLocalMove(difference * 2f, duration);
                            }

                            lastScreen.RectTransform.DOScale(2f, duration);
                            break;
                        case ScreenTransition.Out:
                            lastScreen.RectTransform.DOScale(0.5f, duration);
                            break;
                        case ScreenTransition.Left:
                            lastScreen.RectTransform.DOLocalMove(new Vector3(Context.ReferenceWidth, 0), duration);
                            break;
                        case ScreenTransition.Right:
                            lastScreen.RectTransform.DOLocalMove(new Vector3(-Context.ReferenceWidth, 0), duration);
                            break;
                        case ScreenTransition.Up:
                            lastScreen.RectTransform.DOLocalMove(new Vector3(0, -Context.ReferenceHeight), duration);
                            break;
                        case ScreenTransition.Down:
                            lastScreen.RectTransform.DOLocalMove(new Vector3(0, Context.ReferenceHeight), duration);
                            break;
                        case ScreenTransition.Fade:
                            break;
                    }
                }
            }
        }

        if (!newScreenWasCreated)
        {
            if (DoNotActivateGameObjects)
            {
                if (!newScreen.gameObject.activeSelf) newScreen.gameObject.SetActive(true);
                newScreen.ChildrenCanvases.ForEach(it => it.enabled = true);
                newScreen.ChildrenCanvasGroups.ForEach(it => it.enabled = true);
                newScreen.ChildrenGraphicRaycasters.ForEach(it => it.enabled = true);
            }
            else
            {
                newScreen.gameObject.SetActive(true);
            }
        }
        
        foreach (var listener in screenChangeListeners) listener.OnScreenChangeStarted(lastScreen, newScreen);

        if (newScreenTransitionDelay > 0)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(newScreenTransitionDelay),
                    cancellationToken: (screenChangeCancellationTokenSource = new CancellationTokenSource()).Token);
            }
            catch
            {
                ChangingToScreenId = null;
                return;
            }
        }
        
        if (payload == null) payload = newScreen.GetDefaultPayload();
        newScreen.IntentPayload = payload;
        ActiveScreenId = newScreen.GetId();
        newScreen.CanvasGroup.alpha = 0;
        newScreen.State = ScreenState.Active;
        var blocksRaycasts = newScreen.CanvasGroup.blocksRaycasts;
        newScreen.CanvasGroup.interactable = newScreen.CanvasGroup.blocksRaycasts = blocksRaycasts; // Special handling

        if (disableTransitions)
        {
            newScreen.CanvasGroup.alpha = 1f;
            /*try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(duration),
                    cancellationToken: (screenChangeCancellationTokenSource = new CancellationTokenSource()).Token);
                await UniTask.DelayFrame(4,
                    cancellationToken: (screenChangeCancellationTokenSource = new CancellationTokenSource()).Token); // UI rebuild
            }
            catch
            {
                ChangingToScreenId = null;
                return;
            }*/
            newScreen.RectTransform.localPosition = Vector3.zero;
            newScreen.RectTransform.localScale = new Vector3(1, 1);
            OpaqueOverlay.Hide(duration);
        }
        else
        {
            newScreen.CanvasGroup.alpha = 0f;
            newScreen.CanvasGroup.DOFade(1f, duration);
            newScreen.RectTransform.DOLocalMove(Vector3.zero, duration);

            if (transition != ScreenTransition.None)
            {
                switch (transition)
                {
                    case ScreenTransition.In:
                        newScreen.RectTransform.localScale = new Vector3(0.5f, 0.5f);
                        newScreen.RectTransform.DOScale(1f, duration);
                        break;
                    case ScreenTransition.Out:
                        newScreen.RectTransform.localScale = new Vector3(2, 2);
                        newScreen.RectTransform.DOScale(1f, duration);
                        break;
                    case ScreenTransition.Left:
                        newScreen.RectTransform.localPosition = new Vector3(-Context.ReferenceWidth, 0);
                        newScreen.RectTransform.localScale = new Vector3(1, 1);
                        break;
                    case ScreenTransition.Right:
                        newScreen.RectTransform.localPosition = new Vector3(Context.ReferenceWidth, 0);
                        newScreen.RectTransform.localScale = new Vector3(1, 1);
                        break;
                    case ScreenTransition.Up:
                        newScreen.RectTransform.localPosition = new Vector3(0, Context.ReferenceHeight);
                        newScreen.RectTransform.localScale = new Vector3(1, 1);
                        break;
                    case ScreenTransition.Down:
                        newScreen.RectTransform.localPosition = new Vector3(0, -Context.ReferenceHeight);
                        newScreen.RectTransform.localScale = new Vector3(1, 1);
                        break;
                    case ScreenTransition.Fade:
                        break;
                }
            }
        }

        void Action()
        {
            ChangingToScreenId = null;

            if (lastScreen != null && lastScreen != newScreen)
            {
                if (DoNotActivateGameObjects)
                {
                    lastScreen.ChildrenCanvases.ForEach(it => it.enabled = false);
                    lastScreen.ChildrenCanvasGroups.ForEach(it => it.enabled = false);
                    lastScreen.ChildrenGraphicRaycasters.ForEach(it => it.enabled = false);
                }
                else
                {
                    lastScreen.gameObject.SetActive(false);
                }

                if (willDestroy) DestroyScreen(lastScreen.GetId());
            }

            onFinished?.Invoke(newScreen);
            foreach (var listener in screenChangeListeners)
                    listener.OnScreenChangeFinished(lastScreen, newScreen);
        }

        if (duration > 0) Run.After(duration, Action);
        else Action();
        
        if (addTargetScreenToHistory)
        {
            print($"Adding {newScreen.GetId()} to history");
            History.Push(new Intent(newScreen.GetId(), payload));
        }
    }

    public void AddHandler(ScreenChangeListener listener)
    {
        screenChangeListeners.Add(listener);
    }

    public void RemoveHandler(ScreenChangeListener listener)
    {
        screenChangeListeners.Remove(listener);
    }

    public static bool DoNotActivateGameObjects => SceneManager.GetActiveScene().name == "Navigation";
}

public class Intent
{
    public string ScreenId { get; }
    public ScreenPayload Payload { get; }

    public Intent(string screenId, ScreenPayload payload)
    {
        ScreenId = screenId;
        Payload = payload;
    }

}