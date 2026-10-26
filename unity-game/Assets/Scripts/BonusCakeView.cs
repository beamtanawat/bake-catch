using UnityEngine;
using UnityEngine.UI;

// Bonus Time cake-cutting scene built from the BonusTime sprite set.
// Presentation only: GameSession decides every slice; this view reacts to BonusSlices/BonusSeconds
// changes in the same frame and never creates a slice, points or time itself.
public sealed class BonusCakeView
{
    // Layout in the 1600x900 UI space. The knife tip in chef_03_slice sits at (0.635, 0.80) of the
    // chef canvas, so the cake's top surface is placed on that point.
    const float ChefSize = 480, ChefLeft = 320, ChefTop = 270;
    static readonly Vector2 KnifeTip = new Vector2(ChefLeft + .635f * ChefSize, ChefTop + .8f * ChefSize);
    const float CakeSize = 272, TableWidth = 1100, TableHeight = 550;
    // Cake canvas: top surface at 32% height, plate bottom at 90.7%.
    static readonly Vector2 CakeTopLeft = new Vector2(KnifeTip.x - .3f * CakeSize, KnifeTip.y - .32f * CakeSize);
    const float CakePlateBase = .907f;
    static readonly Vector2 TimerPos = new Vector2(900, 312);
    // How many slices finish one cake (whole -> cut lines -> separating -> sliced). Scoring is separate.
    public static int CutsPerCake = 3;
    // Chop timing: raise, slice, finish; then a short wait for more input before recover -> ready.
    // A new slice restarts at raise immediately, so fast shaking loops raise -> slice -> finish.
    public static float Raise = .025f, SliceHit = .035f, Finish = .025f, RecoverDelay = .02f, Recover = .05f;
    const float FinishedCakeHold = .25f, SlashTime = .12f, PopTime = .45f, SquashTime = .06f, TimePopTime = .5f;
    const int PieceBudget = 72, CrumbsPerSlice = 8, ExtraCrumbsPerCake = 12, Pops = 4;
    // Per-frame horizontal correction (canvas px) for small drift between separately drawn poses.
    static readonly float[] ChefOffsetX = { 0, 0, 0, -13, 0 };

    static readonly string[] ChefFrames = { "chef_01_ready", "chef_02_raise", "chef_03_slice", "chef_04_finish", "chef_05_recover" };
    static readonly string[] CakeStates = { "cake_01_whole", "cake_02_cut_lines", "cake_03_slice_separating", "cake_04_sliced" };
    static readonly Color[] CrumbColors = { new Color(1f, .96f, .9f), new Color(.96f, .79f, .5f), new Color(.98f, .64f, .73f) };

    readonly RectTransform root, chef, cake, slash, timer, timePop;
    readonly Image chefImage, cakeImage, slashImage;
    readonly Text status, tally, timerText, timePopText;
    readonly Sprite[] chefSprites = new Sprite[ChefFrames.Length], cakeSprites = new Sprite[CakeStates.Length];
    readonly Sprite sliceSprite, starSprite;

    // Small fixed pools: crumbs, cake bits and stars share one budget; the oldest is reused when full.
    readonly RectTransform[] pieces = new RectTransform[PieceBudget];
    readonly Image[] pieceImages = new Image[PieceBudget];
    readonly Vector2[] pieceVelocity = new Vector2[PieceBudget];
    readonly float[] pieceAge = new float[PieceBudget], pieceLife = new float[PieceBudget], pieceSpin = new float[PieceBudget];
    readonly RectTransform[] pops = new RectTransform[Pops];
    readonly Text[] popTexts = new Text[Pops];
    readonly float[] popAge = new float[Pops];
    readonly Vector2[] popStart = new Vector2[Pops];

    int shownSlices, shownSeconds, cutsOnCake, nextPiece, nextPop, slashSide;
    float chopClock = 99f, slashAge = 99f, squashAge = 99f, finishedAge = -1f, shakeAge = 99f, timeAge = 99f;

    public BonusCakeView(Transform parent, Font font)
    {
        root = Box("Cake scene", parent, 0, 0, 1600, 900);
        for (int i = 0; i < ChefFrames.Length; i++) chefSprites[i] = BakeryArt.Artwork("BonusTime/" + ChefFrames[i]);
        for (int i = 0; i < CakeStates.Length; i++) cakeSprites[i] = BakeryArt.Artwork("BonusTime/" + CakeStates[i]);
        sliceSprite = BakeryArt.Artwork("BonusTime/cake_slice_single");
        starSprite = BakeryArt.Icon(9);

        // Back to front: chef -> worktable (hides the legs) -> cake -> slash and crumbs -> score and UI.
        chefImage = Img("Chef", root, ChefLeft, ChefTop, ChefSize, ChefSize, chefSprites[0]);
        chef = chefImage.rectTransform;
        Pivot(chef, new Vector2(.5f, 0));

        float tableTop = KnifeTip.y + 50 - .174f * TableHeight;
        Img("Worktable", root, KnifeTip.x - TableWidth * .5f, tableTop, TableWidth, TableHeight, BakeryArt.Artwork("BonusTime/worktable"));

        cakeImage = Img("Cake", root, CakeTopLeft.x, CakeTopLeft.y, CakeSize, CakeSize, cakeSprites[0]);
        cake = cakeImage.rectTransform;
        Pivot(cake, new Vector2(.5f, 1f - CakePlateBase));

        slashImage = Img("Knife slash", root, KnifeTip.x - 150, KnifeTip.y - 170, 260, 260, BakeryArt.Artwork("BonusTime/knife_slash"));
        slash = slashImage.rectTransform;
        Pivot(slash, new Vector2(.72f, .3f));

        for (int i = 0; i < PieceBudget; i++)
        {
            pieceImages[i] = Img("Cake bit " + i, root, 0, 0, 16, 16, BakeryArt.Circle);
            pieces[i] = pieceImages[i].rectTransform;
            Pivot(pieces[i], new Vector2(.5f, .5f));
        }

        for (int i = 0; i < Pops; i++)
        {
            popTexts[i] = Txt("Score pop " + i, root, font, "", 0, 0, 200, 70, 46, BakeryArt.Hex("#FF537D"));
            pops[i] = popTexts[i].rectTransform;
            Pivot(pops[i], new Vector2(.5f, .5f));
        }

        Img("Bonus banner", root, 570, 132, 460, 153, BakeryArt.Artwork("BonusTime/bonus_time_banner"));
        // Bonus countdown: the single GameSession.BonusRemaining value, formatted only here.
        timer = Box("Bonus timer", root, TimerPos.x - 95, TimerPos.y - 24, 190, 48);
        Pivot(timer, new Vector2(.5f, .5f));
        Panel(timer, "Timer rim", 0, 0, 190, 48, BakeryArt.Hex("#F4AA32"));
        Panel(timer, "Timer face", 4, 4, 182, 40, BakeryArt.Hex("#FFF8EB"));
        timerText = Txt("Timer text", timer, font, "", 0, 0, 190, 48, 30, BakeryArt.Hex("#E2356A"));
        tally = Txt("Bonus tally", root, font, "", TimerPos.x - 200, TimerPos.y + 28, 400, 32, 21, BakeryArt.Hex("#592A24"));
        timePopText = Txt("Time pop", root, font, "", 0, 0, 160, 60, 40, BakeryArt.Hex("#18B58C"));
        timePop = timePopText.rectTransform;
        Pivot(timePop, new Vector2(.5f, .5f));

        // Right-hand shake hint with the real device state; the icon is illustrative, not the sensor model.
        Panel(root, "Shake card shadow", 1176, 318, 290, 340, new Color(.35f, .13f, .08f, .22f));
        Panel(root, "Shake card rim", 1170, 310, 290, 340, BakeryArt.Hex("#7CC4F2"));
        Panel(root, "Shake card", 1176, 316, 278, 328, BakeryArt.Hex("#FFF8EB"));
        Img("Shake right hand", root, 1225, 322, 180, 180, BakeryArt.Artwork("BonusTime/shake_right_hand"));
        Txt("Shake title", root, font, "SHAKE RIGHT HAND", 1180, 505, 270, 36, 25, BakeryArt.Hex("#592A24"));
        Txt("Shake thai", root, font, "เขย่ามือขวาเพื่อหั่นเค้ก", 1180, 540, 270, 30, 20, BakeryArt.Hex("#B5651D"));
        status = Txt("Device status", root, font, "", 1182, 572, 266, 64, 17, BakeryArt.Hex("#2F6FA3"));
        status.horizontalOverflow = HorizontalWrapMode.Wrap;

        foreach (Text text in new[] { tally, timePopText, popTexts[0], popTexts[1], popTexts[2], popTexts[3] })
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = BakeryArt.Hex("#FFF8EB");
            outline.effectDistance = new Vector2(3, -3);
        }
        Reset();
    }

    // Clears every transient effect and pose; called when Bonus Time starts and when it ends.
    public void Reset()
    {
        shownSlices = shownSeconds = cutsOnCake = 0;
        chopClock = slashAge = squashAge = shakeAge = timeAge = 99f;
        finishedAge = -1f;
        cakeImage.sprite = cakeSprites[0];
        chefImage.sprite = chefSprites[0];
        chef.localRotation = Quaternion.identity;
        cake.localScale = timer.localScale = Vector3.one;
        root.anchoredPosition = Vector2.zero;
        slash.gameObject.SetActive(false);
        timePop.gameObject.SetActive(false);
        for (int i = 0; i < PieceBudget; i++) { pieces[i].gameObject.SetActive(false); pieceAge[i] = pieceLife[i] = 0f; }
        for (int i = 0; i < Pops; i++) { pops[i].gameObject.SetActive(false); popAge[i] = PopTime; }
    }

    // dt is 0 while paused, so the pose, cake and effects hold their current frame.
    public void Tick(GameSession session, PlayerInputProvider input, float dt)
    {
        // React in the same frame the session accepted the input; several slices in one frame
        // become one visual hit showing their real total.
        int newSlices = session.BonusSlices - shownSlices;
        if (newSlices > 0) { shownSlices = session.BonusSlices; Hit(newSlices); }
        int newSeconds = session.BonusSeconds - shownSeconds;
        if (newSeconds > 0) { shownSeconds = session.BonusSeconds; ShowTimeBonus(newSeconds); }

        chopClock += dt;
        int frame = ChefFrame(chopClock);
        chefImage.sprite = chefSprites[frame];
        chef.anchoredPosition = new Vector2(ChefLeft + ChefSize * .5f + ChefOffsetX[frame] * ChefSize / 1254f, -(ChefTop + ChefSize));
        chef.localRotation = Quaternion.Euler(0, 0, frame == 2 ? -2f : frame == 3 ? -1f : 0f);

        squashAge += dt;
        float squash = squashAge < SquashTime ? Mathf.Sin(squashAge / SquashTime * Mathf.PI) : 0f;
        cake.localScale = new Vector3(1f + .015f * squash, 1f - .045f * squash, 1f);

        if (finishedAge >= 0f)
        {
            finishedAge += dt;
            if (finishedAge >= FinishedCakeHold) { finishedAge = -1f; cutsOnCake = 0; cakeImage.sprite = cakeSprites[0]; }
        }

        // Small shake of the bonus scene only (never the HUD), after a finished cake.
        shakeAge += dt;
        root.anchoredPosition = shakeAge < .12f && dt > 0f ? new Vector2(Random.Range(-3f, 3f), Random.Range(-3f, 3f)) * (1f - shakeAge / .12f) : Vector2.zero;

        slashAge += dt;
        slash.gameObject.SetActive(slashAge < SlashTime);
        if (slashAge < SlashTime)
        {
            float t = slashAge / SlashTime;
            slash.localScale = Vector3.one * Mathf.Lerp(.7f, 1.15f, 1f - (1f - t) * (1f - t));
            slashImage.color = new Color(1, 1, 1, t < .2f ? t / .2f : 1f - (t - .2f) / .8f);
        }

        for (int i = 0; i < PieceBudget; i++)
        {
            if (!pieces[i].gameObject.activeSelf) continue;
            pieceAge[i] += dt;
            float t = pieceAge[i] / pieceLife[i];
            if (t >= 1f) { pieces[i].gameObject.SetActive(false); continue; }
            pieceVelocity[i] += new Vector2(0, -1700f) * dt;
            pieces[i].anchoredPosition += pieceVelocity[i] * dt;
            pieces[i].localRotation = Quaternion.Euler(0, 0, pieces[i].localEulerAngles.z + pieceSpin[i] * dt);
            float fade = t < .55f ? 1f : 1f - (t - .55f) / .45f;
            pieces[i].localScale = Vector3.one * Mathf.Lerp(1f, .55f, Mathf.Clamp01((t - .4f) / .6f));
            Color c = pieceImages[i].color;
            pieceImages[i].color = new Color(c.r, c.g, c.b, fade);
        }

        for (int i = 0; i < Pops; i++)
        {
            if (popAge[i] >= PopTime) continue;
            popAge[i] += dt;
            float t = Mathf.Clamp01(popAge[i] / PopTime);
            pops[i].gameObject.SetActive(t < 1f);
            pops[i].anchoredPosition = popStart[i] + new Vector2(0, 45f * (1f - (1f - t) * (1f - t)));
            pops[i].localScale = Vector3.one * (t < .2f ? Mathf.Lerp(.6f, 1.15f, t / .2f) : 1.15f - .15f * t);
            Color c = popTexts[i].color;
            popTexts[i].color = new Color(c.r, c.g, c.b, t < .65f ? 1f : 1f - (t - .65f) / .35f);
        }

        timeAge += dt;
        timePop.gameObject.SetActive(timeAge < TimePopTime);
        float tp = Mathf.Clamp01(timeAge / TimePopTime);
        timer.localScale = Vector3.one * (timeAge < .18f ? 1f + .14f * Mathf.Sin(timeAge / .18f * Mathf.PI) : 1f);
        if (timeAge < TimePopTime)
        {
            timePop.anchoredPosition = new Vector2(TimerPos.x + 135, -TimerPos.y + 38f * (1f - (1f - tp) * (1f - tp)));
            timePop.localScale = Vector3.one * (tp < .2f ? Mathf.Lerp(.7f, 1.2f, tp / .2f) : 1.2f - .2f * tp);
            timePopText.color = new Color(.09f, .71f, .55f, tp < .6f ? 1f : 1f - (tp - .6f) / .4f);
        }

        int seconds = Mathf.CeilToInt(session.BonusRemaining);
        timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        tally.text = $"Cuts {session.BonusSlices}   •   +{session.BonusSlices * GameSession.SliceScore}" +
            (GameSession.MaxBonusSeconds > 0 ? $"   •   Time +{session.BonusSeconds}/{GameSession.MaxBonusSeconds}s" : "");
        status.text = input != null && input.ShakeSensorLive ? "Shake sensor connected" :
            input != null && input.SensorLive ? "Grip sensor only — press Space to cut" : "No sensor — press Space to cut";
    }

    static int ChefFrame(float clock)
    {
        if (clock < Raise) return 1;
        if (clock < Raise + SliceHit) return 2;
        if (clock < Raise + SliceHit + Finish + RecoverDelay) return 3;
        if (clock < Raise + SliceHit + Finish + RecoverDelay + Recover) return 4;
        return 0;
    }

    // Feedback for slices the session already scored: restart the chop and play effects now.
    void Hit(int count)
    {
        chopClock = 0f;
        slashAge = 0f;
        squashAge = 0f;
        slashSide = 1 - slashSide;
        slash.localRotation = Quaternion.Euler(0, 0, slashSide == 0 ? -10f : 6f);
        Pop("+" + count * GameSession.SliceScore);

        bool finished = false;
        for (int i = 0; i < count; i++)
        {
            if (finishedAge >= 0f) { finishedAge = -1f; cutsOnCake = 0; }
            cutsOnCake = Mathf.Min(cutsOnCake + 1, CutsPerCake);
            if (cutsOnCake >= CutsPerCake) { finishedAge = 0f; finished = true; }
        }
        cakeImage.sprite = cakeSprites[Mathf.Clamp(Mathf.CeilToInt(3f * cutsOnCake / CutsPerCake), 1, 3)];

        Vector2 contact = new Vector2(KnifeTip.x + 18, -(KnifeTip.y + 6));
        for (int i = 0; i < Mathf.Min(count, 2) * CrumbsPerSlice; i++)
            Spawn(contact, i % 3 == 0 ? sliceSprite : BakeryArt.Circle, i % 3 == 0 ? Random.Range(20f, 34f) : Random.Range(7f, 15f),
                Random.Range(.25f, .55f), 1f);
        if (!finished) return;
        // Finished cake: a slightly bigger burst, two larger slices and a few stars.
        shakeAge = 0f;
        for (int i = 0; i < ExtraCrumbsPerCake; i++)
            Spawn(contact, BakeryArt.Circle, Random.Range(8f, 17f), Random.Range(.35f, .6f), 1.25f);
        for (int i = 0; i < 2; i++) Spawn(contact + new Vector2(30, 20), sliceSprite, 64f, .6f, .8f);
        for (int i = 0; i < 4; i++) Spawn(contact + new Vector2(0, 30), starSprite, Random.Range(26f, 38f), .5f, 1.1f);
    }

    void Spawn(Vector2 from, Sprite sprite, float size, float life, float power)
    {
        int i = nextPiece++ % PieceBudget;
        pieces[i].gameObject.SetActive(true);
        pieceImages[i].sprite = sprite;
        pieceImages[i].preserveAspect = true;
        pieceImages[i].color = sprite == BakeryArt.Circle ? CrumbColors[Random.Range(0, CrumbColors.Length)] : Color.white;
        pieces[i].sizeDelta = new Vector2(size, size);
        pieces[i].anchoredPosition = from;
        pieces[i].localScale = Vector3.one;
        pieces[i].localRotation = Quaternion.Euler(0, 0, Random.Range(-30f, 30f));
        // Fan out sideways and up from the knife contact, then fall in an arc.
        float angle = Random.Range(25f, 155f) * Mathf.Deg2Rad;
        pieceVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(320f, 680f) * power;
        pieceSpin[i] = Random.Range(-420f, 420f);
        pieceAge[i] = 0f;
        pieceLife[i] = life;
    }

    void Pop(string value)
    {
        int i = nextPop++ % Pops;
        popTexts[i].text = value;
        popAge[i] = 0f;
        // Alternate sides and heights so quick pops stay readable instead of stacking.
        popStart[i] = new Vector2(KnifeTip.x + 80 + (i % 2 == 0 ? -45f : 45f) + Random.Range(-10f, 10f), -(KnifeTip.y - 70) + (i / 2) * 22f + Random.Range(-6f, 6f));
        pops[i].gameObject.SetActive(true);
    }

    void ShowTimeBonus(int seconds)
    {
        timeAge = 0f;
        timePopText.text = "+" + seconds + "s";
    }

    static void Pivot(RectTransform rect, Vector2 pivot)
    {
        Vector2 shift = new Vector2((pivot.x - rect.pivot.x) * rect.sizeDelta.x, (pivot.y - rect.pivot.y) * rect.sizeDelta.y);
        rect.pivot = pivot;
        rect.anchoredPosition += shift;
    }

    static RectTransform Box(string name, Transform parent, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    // Fixed-size rect with preserved aspect: frames swap sprites without SetNativeSize, so nothing jumps.
    static Image Img(string name, Transform parent, float x, float y, float width, float height, Sprite sprite)
    {
        Image image = Box(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    static void Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        Image image = Box(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.sprite = BakeryArt.Round;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
    }

    static Text Txt(string name, Transform parent, Font font, string value, float x, float y, float width, float height, int size, Color color)
    {
        Text text = Box(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.text = value;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
