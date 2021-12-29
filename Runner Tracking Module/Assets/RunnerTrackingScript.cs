using KModkit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class RunnerTrackingScript : MonoBehaviour
{
    static int _moduleIdCounter = 1;
    int _moduleID = 0;

    public KMBombModule Module;
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public SpriteRenderer[] SpriteRends; //Ground = 0, Flag = 1, Base runner = 2, Left number = 3, Right number = 4, Go button = 5
    public Sprite[] Runners; //Main = 0, Not main = 1, Selected = 2
    public Sprite[] Flags; //Start = 0, Finish = 1
    public Sprite[] Digits; //1 = 0, 2 = 1, etc.
    public Sprite[] GoButtonLabels; //Wait = 0, Ready = 1
    public KMSelectable[] Buttons;
    public MeshRenderer Screen;
    public GameObject SpritesParent;

    private List<SpriteRenderer> NotMainRunners = new List<SpriteRenderer>();
    private SpriteRenderer SecondRoad;
    private List<Coroutine> RunningAnims = new List<Coroutine>();
    private int Stage;
    private int[][] RunnerStates = new int[][] { new int[110], new int[110], new int[110] };
    private int[] Positions = new int[3];
    private float MainSpeed;
    private float RunningSpeedMain = 0.15f;
    private float RunningSpeedNotMain = 0.15f;
    private float RunnerJumpHeight = 0.025f;
    private float ButtonDepression = 0.0025f;
    private float ButtonPressDuration = 0.075f;
    private List<bool>[] Movements = { new List<bool>(), new List<bool>(), new List<bool>() }; //false = back, true = forwards
    private bool Active, Playing, Ready, Solved;

    enum SRs //(Sprite Renderers)
    {
        Road,
        Flag,
        Runner,
        LeftNumber,
        RightNumber,
        GoButton
    }

    enum RSs //(Runner Sprites)
    {
        Main,
        NotMain,
        Selected
    }

    enum Fs //(Flags)
    {
        Start,
        Finish
    }

    enum GBLs //(Go button labels)
    {
        Wait,
        Ready
    }

    void Awake()
    {
        _moduleID = _moduleIdCounter++;
        for (int i = 0; i < Buttons.Length; i++)
        {
            int x = i;
            Buttons[x].OnInteract += delegate { ButtonPress(x); return false; };
        }
        Module.OnActivate += delegate { Activate(); };
    }

    // Use this for initialization
    void Start()
    {
        SpritesParent.transform.localPosition = new Vector3(SpritesParent.transform.localPosition.x, -0.15f, SpritesParent.transform.localPosition.z);
        SpriteRends[(int)SRs.LeftNumber].sprite = null;
        SpriteRends[(int)SRs.RightNumber].sprite = null;
        SpriteRends[(int)SRs.GoButton].sprite = GoButtonLabels[(int)GBLs.Wait];

        #region Turn off the screen and buttons
        Screen.material.color = new Color();
        for (int i = 0; i < Buttons.Length; i++)
        {
            Buttons[i].GetComponent<MeshRenderer>().material.color = new Color();
            Buttons[i].GetComponentInChildren<SpriteRenderer>().material.color = new Color();
        }
        #endregion
    }

    void Activate()
    {
        Active = true;
        SpritesParent.transform.localPosition = new Vector3(SpritesParent.transform.localPosition.x, 0, SpritesParent.transform.localPosition.z); //Reveal sprites

        #region Turn the screen and buttons back on
        Screen.material.color = new Color(1, 1, 1);
        for (int i = 0; i < Buttons.Length; i++)
        {
            Buttons[i].GetComponent<MeshRenderer>().material.color = new Color(1, 1, 1);
            Buttons[i].GetComponentInChildren<SpriteRenderer>().material.color = new Color(1, 1, 1);
        }
        #endregion

        #region Set up base sprite positions
        SpriteRends[(int)SRs.Road].transform.localPosition = new Vector3(0, 0.55f, -0.145f);
        SpriteRends[(int)SRs.Flag].transform.localPosition = new Vector3(-0.021f, 0.55f, 0.078f);
        SpriteRends[(int)SRs.Runner].transform.localPosition = new Vector3(-0.2f, 0.55f, -0.0206f);
        SpriteRends[(int)SRs.Runner].transform.localEulerAngles = new Vector3(90, 10, 0);
        #endregion

        #region Instantiate road and set its position, rotation and scale, then call "MoveRoad"
        SecondRoad = Instantiate(SpriteRends[(int)SRs.Road], SpriteRends[(int)SRs.Road].transform.localPosition, SpriteRends[(int)SRs.Road].transform.localRotation);
        SecondRoad.transform.parent = SpriteRends[(int)SRs.Road].transform.parent;
        SecondRoad.transform.localScale = SpriteRends[(int)SRs.Road].transform.localScale;
        SecondRoad.transform.localPosition = new Vector3(1f, 0.55f, -0.145f);
        StartCoroutine(MoveRoad());
        #endregion

        #region Instantiate runners and set their position, rotation and scale, then call "AnimateRunners" for each
        RunningAnims.Add(StartCoroutine(AnimateRunners(true)));
        for (int i = 0; i < 3; i++)
        {
            NotMainRunners.Add(Instantiate(SpriteRends[(int)SRs.Runner], SpriteRends[(int)SRs.Runner].transform.localPosition, SpriteRends[(int)SRs.Runner].transform.localRotation));
            NotMainRunners[i].transform.parent = SpriteRends[(int)SRs.Runner].transform.parent;
            NotMainRunners[i].sprite = Runners[(int)RSs.NotMain];
            NotMainRunners[i].transform.localScale = SpriteRends[(int)SRs.Runner].transform.localScale;
            NotMainRunners[i].transform.localPosition = SpriteRends[(int)SRs.Runner].transform.localPosition;
            NotMainRunners[i].transform.localPosition -= new Vector3((i + 1) * 0.125f, 0, 0);
            RunningAnims.Add(StartCoroutine(AnimateRunners(false, i)));
        }
        #endregion
        StartCoroutine(Calculate());
    }

    void ButtonPress(int pos)
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonRelease, Buttons[pos].transform);
        StartCoroutine(ButtonAnim(pos));
        if (Active && Ready)
        {
            if (pos == 8 && !Playing)
                StartCoroutine(StartTimer());
        }
    }

    private IEnumerator StartTimer()
    {
        Playing = true;
        Audio.PlaySoundAtTransform("intro count 3", SpriteRends[(int)SRs.LeftNumber].transform);
        SpriteRends[(int)SRs.LeftNumber].sprite = Digits[2];
        float duration = 0.625f;
        float timer = 0;
        for (int i = 2; i > 0; i--)
        {
            while (timer < duration)
            {
                yield return null;
                timer += Time.deltaTime;
            }
            Audio.PlaySoundAtTransform("intro count " + i, SpriteRends[(int)SRs.LeftNumber].transform);
            SpriteRends[(int)SRs.LeftNumber].sprite = Digits[i - 1];
            timer = 0;
        }
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
        }
        SpriteRends[(int)SRs.LeftNumber].sprite = null;
        StartCoroutine(Introduce());
    }

    private IEnumerator Introduce()
    {
        RunningSpeedMain = 0.1f;
        RunningSpeedMain = 0.125f;
        SpriteRends[(int)SRs.Runner].transform.localEulerAngles = new Vector3(90, 15, 0);
        List<float> notMainStarts = new List<float>();
        for (int i = 0; i < NotMainRunners.Count; i++)
        {
            NotMainRunners[i].transform.localEulerAngles = new Vector3(90, 15, 0);
            notMainStarts.Add(NotMainRunners[i].transform.localPosition.x);
        }

        float duration = 0.5f;
        float timer = 0;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            SpriteRends[(int)SRs.Runner].transform.localPosition = new Vector3(Mathf.Lerp(-0.2f, 0, timer * (1 / duration)), SpriteRends[(int)SRs.Runner].transform.localPosition.y, SpriteRends[(int)SRs.Runner].transform.localPosition.z);
            for (int i = 0; i < NotMainRunners.Count; i++)
                NotMainRunners[i].transform.localPosition = new Vector3(Mathf.Lerp(notMainStarts[i], notMainStarts[i] + 0.05f, timer * (1 / duration)), NotMainRunners[i].transform.localPosition.y, NotMainRunners[i].transform.localPosition.z);
        }
        float flagOffScreen = -0.6f;
        MainSpeed = 0.58f;
        timer = 0;
        duration = 1f;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            for (int i = 0; i < NotMainRunners.Count; i++)
                NotMainRunners[i].transform.localPosition = new Vector3(Mathf.Lerp(notMainStarts[i] + 0.0625f, flagOffScreen - ((i + 1) * 0.125f), SpriteRends[(int)SRs.Flag].transform.localPosition.x * (1 / flagOffScreen)), NotMainRunners[i].transform.localPosition.y, NotMainRunners[i].transform.localPosition.z);
            SpriteRends[(int)SRs.Flag].transform.localPosition = new Vector3(Mathf.Lerp(-0.021f, flagOffScreen, timer * (1 / duration)), SpriteRends[(int)SRs.Flag].transform.localPosition.y, SpriteRends[(int)SRs.Flag].transform.localPosition.z);
        }
        MainSpeed = 0.3f;
        SpriteRends[(int)SRs.Runner].transform.localEulerAngles = new Vector3(90, 10, 0);
        RunningSpeedMain = 0.15f;
        for (int i = 0; i < NotMainRunners.Count; i++)
        {
            for (int j = 1; j < NotMainRunners.Count + 1; j++)
                StopCoroutine(RunningAnims[j]);
            Destroy(NotMainRunners[i]);
        }
        StartCoroutine(PassRunners());
    }

    private IEnumerator PassRunners()
    {
        throw new NotImplementedException();
    }

    private IEnumerator Calculate()
    {
        yield return null;
        int cache = 2;
        int current = 1;
        for (int i = 0; i < 3; i++)
        {
            switch (i)
            {
                case 0:
                    for (int j = 0; j < RunnerStates[0].Length; j++)
                        RunnerStates[0][j] = 1;
                    GenerateStageOne:
                    yield return null;
                    int passes = Rnd.Range(10, 15);
                    Movements[0].Add(false);
                    for (int j = 1; j < passes; j++)
                    {
                        if (cache == 2)
                            Movements[0].Add(false);
                        else
                            Movements[0].Add(Rnd.Range(0, 2) == 0 ? false : true);
                        if (Movements[0][j])
                            cache--;
                        else
                            cache++;
                    }
                    if (cache > 8 || cache < 1)
                    {
                        Movements[0] = new List<bool>();
                        cache = 2;
                        goto GenerateStageOne;
                    }
                    Positions[2] = cache + 1;
                    Debug.Log(Movements[0].Join(", "));
                    Debug.Log(RunnerStates[0].Join());
                    cache = 2;
                    break;
                case 1:
                    current = 1;
                    for (int j = 0; j < RunnerStates[1].Length; j++)
                        RunnerStates[1][j] = (Rnd.Range(0, 4) % 3) + 1;
                    RunnerStates[1][0] = 1;
                    GenerateStageTwo:
                    yield return null;
                    int passes2 = Rnd.Range(20, 25);
                    Movements[1].Add(false);
                    for (int j = 1; j < passes2; j++)
                    {
                        if (cache == 2)
                            Movements[1].Add(false);
                        else
                            Movements[1].Add(Rnd.Range(0, 2) == 0 ? false : true);
                        try
                        {
                            if (Movements[1][j])
                            {
                                cache -= RunnerStates[1][current];
                                current--;
                            }
                            else
                            {
                                cache += RunnerStates[1][current + 1];
                                current++;
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            Movements[1] = new List<bool>();
                            cache = 2;
                            goto GenerateStageTwo;
                        }
                    }
                    if (cache > 8 || cache < 1)
                    {
                        Movements[1] = new List<bool>();
                        cache = 2;
                        goto GenerateStageTwo;
                    }
                    Positions[2] = cache + 1;
                    Debug.Log(Movements[1].Join(", "));
                    Debug.Log(RunnerStates[1].Join());
                    cache = 2;
                    break;
                default:
                    current = 1;
                    for (int j = 0; j < RunnerStates[2].Length; j++)
                        RunnerStates[2][j] = Rnd.Range(1, 4);
                    RunnerStates[2][0] = 1;
                    GenerateStageThree:
                    yield return null;
                    int passes3 = Rnd.Range(30, 35);
                    Movements[2].Add(false);
                    for (int j = 1; j < passes3; j++)
                    {
                        if (cache == 2)
                            Movements[2].Add(false);
                        else
                            Movements[2].Add(Rnd.Range(0, 2) == 0 ? false : true);
                        try
                        {
                            if (Movements[2][j])
                            {
                                cache -= RunnerStates[2][current];
                                current--;
                            }
                            else
                            {
                                cache += RunnerStates[2][current + 1];
                                current++;
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            Movements[2] = new List<bool>();
                            cache = 2;
                            goto GenerateStageThree;
                        }
                    }
                    if (cache > 8 || cache < 1)
                    {
                        Movements[2] = new List<bool>();
                        cache = 2;
                        goto GenerateStageThree;
                    }
                    Positions[2] = cache + 1;
                    Debug.Log(Movements[2].Join(", "));
                    Debug.Log(RunnerStates[2].Join());
                    break;
            }
        }
        SpriteRends[(int)SRs.GoButton].sprite = GoButtonLabels[(int)GBLs.Ready];
        Ready = true;
    }

    private IEnumerator ButtonAnim(int pos)
    {
        float timer = 0;
        while (timer < ButtonPressDuration)
        {
            yield return null;
            timer += Time.deltaTime;
            Buttons[pos].transform.localPosition = new Vector3(Buttons[pos].transform.localPosition.x, Easing.InOutSine(timer, 0.00188f, 0.00188f - ButtonDepression, ButtonPressDuration), Buttons[pos].transform.localPosition.z);
        }
        Buttons[pos].transform.localPosition = new Vector3(Buttons[pos].transform.localPosition.x, 0.00188f - ButtonDepression, Buttons[pos].transform.localPosition.z);
        timer = ButtonPressDuration;
        while (timer > 0)
        {
            yield return null;
            timer -= Time.deltaTime;
            Buttons[pos].transform.localPosition = new Vector3(Buttons[pos].transform.localPosition.x, Easing.InOutSine(timer, 0.00188f, 0.00188f - ButtonDepression, ButtonPressDuration), Buttons[pos].transform.localPosition.z);
        }
        Buttons[pos].transform.localPosition = new Vector3(Buttons[pos].transform.localPosition.x, 0.00188f, Buttons[pos].transform.localPosition.z);
    }

    private IEnumerator MoveRoad()
    {
        while (!Solved)
        {
            while (SpriteRends[(int)SRs.Road].transform.localPosition.x > -1f)
            {
                yield return null;
                SpriteRends[(int)SRs.Road].transform.localPosition -= new Vector3(Time.deltaTime * MainSpeed, 0, 0);
                SecondRoad.transform.localPosition -= new Vector3(Time.deltaTime * MainSpeed, 0, 0);
            }
            SpriteRends[(int)SRs.Road].transform.localPosition = new Vector3(0, SpriteRends[(int)SRs.Road].transform.localPosition.y, SpriteRends[(int)SRs.Road].transform.localPosition.z);
            SecondRoad.transform.localPosition = new Vector3(1, SecondRoad.transform.localPosition.y, SecondRoad.transform.localPosition.z);
        }
    }

    private IEnumerator AnimateRunners(bool isMain, int pos = 0)
    {
        float duration = RunningSpeedNotMain;
        if (isMain)
            duration = RunningSpeedMain;
        float timer = 0;
        var movement = new Vector3();
        while (true)
        {
            while (timer < duration)
            {
                yield return null;
                timer += Time.deltaTime;
                movement = new Vector3(isMain ? SpriteRends[(int)SRs.Runner].transform.localPosition.x : NotMainRunners[pos].transform.localPosition.x,
                    isMain ? SpriteRends[(int)SRs.Runner].transform.localPosition.y : NotMainRunners[pos].transform.localPosition.y,
                    Easing.OutSine(timer, RunnerJumpHeight * -1, -0.0106f, duration));
                if (isMain)
                    SpriteRends[(int)SRs.Runner].transform.localPosition = movement;
                else
                    NotMainRunners[pos].transform.localPosition = movement;

                duration = RunningSpeedNotMain;
                if (isMain)
                    duration = RunningSpeedMain;
            }
            if (isMain)
                SpriteRends[(int)SRs.Runner].transform.localPosition = new Vector3(SpriteRends[(int)SRs.Runner].transform.localPosition.x,
                    SpriteRends[(int)SRs.Runner].transform.localPosition.y,
                    -0.0106f);
            else
                NotMainRunners[pos].transform.localPosition = new Vector3(NotMainRunners[pos].transform.localPosition.x,
                    NotMainRunners[pos].transform.localPosition.y,
                    -0.0106f);
            timer = duration;
            while (timer > 0)
            {
                yield return null;
                timer -= Time.deltaTime;
                movement = new Vector3(isMain ? SpriteRends[(int)SRs.Runner].transform.localPosition.x : NotMainRunners[pos].transform.localPosition.x,
                    isMain ? SpriteRends[(int)SRs.Runner].transform.localPosition.y : NotMainRunners[pos].transform.localPosition.y,
                    Easing.OutSine(timer, RunnerJumpHeight * -1, -0.0106f, duration));
                if (isMain)
                    SpriteRends[(int)SRs.Runner].transform.localPosition = movement;
                else
                    NotMainRunners[pos].transform.localPosition = movement;

                duration = RunningSpeedNotMain;
                if (isMain)
                    duration = RunningSpeedMain;
            }
            if (isMain)
                SpriteRends[(int)SRs.Runner].transform.localPosition = new Vector3(SpriteRends[(int)SRs.Runner].transform.localPosition.x,
                    SpriteRends[(int)SRs.Runner].transform.localPosition.y,
                    RunnerJumpHeight * -1);
            else
                NotMainRunners[pos].transform.localPosition = new Vector3(NotMainRunners[pos].transform.localPosition.x,
                    NotMainRunners[pos].transform.localPosition.y,
                    RunnerJumpHeight * -1);
            timer = 0;
        }
    }
}
