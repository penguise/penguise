using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEngine;

public static class GameLayoutChecks
{
    private const float Tolerance = 0.002f;

    public static void Run()
    {
        var layout = UnityEngine.Object.FindAnyObjectByType<GameLayout>();
        Require(layout != null, "GameLayout exists in the open scene");
        Require(!Application.isPlaying, "Run layout checks outside Play mode");
        Vector2[] boundary = DenseBoundary();
        var serialized = new SerializedObject(layout);
        var countProperty = serialized.FindProperty("playerCount");
        int original = countProperty.intValue;
        try
        {
            for (int count = 4; count <= 8; count++)
            {
                serialized.Update();
                countProperty.intValue = count;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                layout.Rebuild();
                var root = layout.transform.Find("Layout");
                Require(layout.transform.childCount == 1, "No stale generated layouts");
                Require(root.childCount == count * 4, "One igloo, doorway mask, heal particle group and character per player");
                var iglooSpacings = new float[count];

                for (int i = 1; i <= count; i++)
                {
                    var character = root.Find("Character " + i);
                    var igloo = root.Find("Igloo " + i);
                    var label = character.GetComponentInChildren<TextMesh>();
                    Require(label.text == "Player " + i, "Automatic player name");
                    Require(label.transform.position.y > character.position.y + GameLayout.CharacterHeight,
                        "Name above character head");

                    // Measure the generated sprites in reference units, independent of the placement math.
                    // Feet: the penguin sprite's pivot (bottom-center of its visible art).
                    Vector2 foot = root.InverseTransformPoint(character.position);
                    Require(Inside(foot), "Feet stay inside the ice outline");
                    Require(character.GetComponent<SpriteRenderer>().flipX == foot.x > GameLayout.LayoutCenter.x + 0.01f,
                        $"Character {i}/{count} faces the middle");
                    var outfit = character.Find("Outfit").GetComponent<SpriteRenderer>();
                    var body = character.GetComponent<SpriteRenderer>();
                    Require(outfit.transform.localPosition == Vector3.zero && outfit.transform.localScale == Vector3.one
                        && outfit.flipX == body.flipX && outfit.sortingOrder > body.sortingOrder
                        && outfit.sortingOrder < label.GetComponent<MeshRenderer>().sortingOrder,
                        $"Outfit {i}/{count} sits exactly on its penguin, between body and name");
                    Require(outfit.color == GameLayout.PlayerColor((LobbyColor)(i - 1)),
                        $"Outfit {i}/{count} uses its player's color");
                    float footGap = Nearest(foot, boundary);
                    Require(Mathf.Abs(footGap - layout.CharacterGap) < Tolerance,
                        $"Character {i}/{count} foot gap {footGap:F4} equals {layout.CharacterGap:F4}");
                    Require(body.sprite.bounds == outfit.sprite.bounds && body.sprite.pivot == outfit.sprite.pivot,
                        "Penguin and outfit art share size and pivot");


                    // Igloo silhouette, measured through the generated transform.
                    Vector2[] iglooShape = Silhouette(root, igloo);
                    float iglooGap = float.PositiveInfinity;
                    foreach (Vector2 point in iglooShape)
                    {
                        Require(!Inside(point), $"Igloo {i}/{count} stays entirely outside the ice");
                        iglooGap = Mathf.Min(iglooGap, Nearest(point, boundary));
                    }
                    Require(Mathf.Abs(iglooGap - layout.IglooGap) < Tolerance,
                        $"Igloo {i}/{count} nearest gap {iglooGap:F4} equals {layout.IglooGap:F4}");
                    Vector2 iglooMin = iglooShape[0], iglooMax = iglooShape[0];
                    foreach (Vector2 point in iglooShape)
                    {
                        iglooMin = Vector2.Min(iglooMin, point);
                        iglooMax = Vector2.Max(iglooMax, point);
                    }
                    Vector2 footDirection = foot - GameLayout.LayoutCenter;
                    float slot = Mathf.Atan2(footDirection.x, footDirection.y) * Mathf.Rad2Deg;
                    Require(AngleError((iglooMin + iglooMax) / 2, slot) < 0.05f,
                        $"Igloo {i}/{count} sits on its character's ray");
                    var window = igloo.GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>(true);
                    Require(window != null && window.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Point,
                        $"Igloo {i}/{count} has a window light");
                    CheckDoor(igloo, $"{i}/{count}");

                    CheckHoleClearance(foot + Vector2.up * GameLayout.CharacterHeight / 2f);
                    var next = root.Find("Character " + (i % count + 1));
                    Require(Vector2.Distance(character.localPosition, next.localPosition) > 1f,
                        "Characters remain separated");
                    var nextIgloo = root.Find("Igloo " + (i % count + 1));
                    Require(Separated(iglooShape, Silhouette(root, nextIgloo), 0.05f),
                        $"Igloos {i} and {i % count + 1} of {count} do not overlap");
                    iglooSpacings[i - 1] = DenseDistance(iglooShape, Silhouette(root, nextIgloo));
                }
                float spacingMin = Mathf.Min(iglooSpacings), spacingMax = Mathf.Max(iglooSpacings);
                Require(spacingMax - spacingMin < 0.01f,
                    $"Edge-to-edge igloo gaps are equal for {count} players ({spacingMin:F3} to {spacingMax:F3})");
                // Runs move the penguins, so they go last.
                for (int i = 1; i <= count; i++)
                    CheckRun(root, i, count);
                CheckIdle(root, count);
                CheckAction(layout, root, count);
                CheckHeal(layout, root, count);
                CheckKnockOut(layout, root, count);
            }
            CheckKnockOutPreview(layout, serialized);
        }
        finally
        {
            serialized.Update();
            countProperty.intValue = original;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            layout.Rebuild();
        }
        CheckDayNightCycle();
        Debug.Log("Game layout checks passed for every player count from 4 to 8.");
    }

    // The door hangs on its hinge inside the igloo's sorting group, closes flush (identity map) and,
    // at every angle, its transform chain reproduces the hinge-centered open map exactly.
    private static void CheckDoor(Transform igloo, string label)
    {
        var door = igloo.GetComponentInChildren<IglooDoor>(true);
        Require(door != null && door.transform.parent == igloo, $"Igloo {label} has a door");
        var group = igloo.GetComponent<UnityEngine.Rendering.SortingGroup>();
        var leaf = door.GetComponentInChildren<SpriteRenderer>(true);
        Require(group != null && leaf != null && leaf.sortingOrder > igloo.GetComponent<SpriteRenderer>().sortingOrder,
            $"Door {label} draws over its igloo inside the igloo's sorting group");
        float original = door.Open;
        try
        {
            foreach (float open in new[] { 0f, 0.3f, 0.55f, 0.7f, 1f })
            {
                door.Open = open;
                Matrix4x4 expected = Matrix4x4.Translate(door.transform.localPosition) * IglooDoor.OpenMatrix(door.Angle);
                Matrix4x4 actual = igloo.worldToLocalMatrix * leaf.transform.localToWorldMatrix;
                for (int k = 0; k < 16; k++)
                    if (k != 10)
                        Require(Mathf.Abs(expected[k] - actual[k]) < 1e-4f, $"Door {label} transform at {open:F2} matches the open map");
                Require(leaf.sprite != null && leaf.flipX == false, $"Door {label} shows a face");
            }
            door.Open = 0f;
            Require(IglooDoor.OpenMatrix(0f) == Matrix4x4.identity || Approximately(IglooDoor.OpenMatrix(0f), Matrix4x4.identity),
                $"Door {label} closed map is identity");
            Require(IglooDoor.FacesCamera(64f) && !IglooDoor.FacesCamera(66f), "Door shows its back past 65 degrees");
        }
        finally
        {
            door.Open = original;
        }
    }

    // Run into the igloo: frames match the idle art, the route keeps clear of every dome, sorts by depth,
    // ends in its own tunnel behind the doorway mask; the door opens for it and closes behind it.
    // Run out: the same route back to the slot, door opening and closing again, idle pose restored.
    // A run in that turns around midway also ends back on the slot.
    private static void CheckRun(Transform root, int slot, int count)
    {
        string label = $"{slot}/{count}";
        var character = root.Find("Character " + slot);
        var runner = character.GetComponent<PenguinRunner>();
        var body = character.GetComponent<SpriteRenderer>();
        var outfit = character.Find("Outfit").GetComponent<SpriteRenderer>();
        var name = character.GetComponentInChildren<TextMesh>().GetComponent<MeshRenderer>();
        var igloo = root.Find("Igloo " + slot);
        var iglooSprite = igloo.GetComponent<SpriteRenderer>().sprite;
        var group = igloo.GetComponent<UnityEngine.Rendering.SortingGroup>();
        var mask = root.Find("Doorway " + slot)?.GetComponent<SpriteMask>();
        Require(runner != null && runner.Current == PenguinRunner.State.Idle && runner.Door != null
            && runner.Door.transform.parent == igloo, $"Penguin {label} has an idle runner bound to its own door");
        // The still pose the runner restores on the slot (the idle animation then takes over).
        Sprite idleBody = (Sprite)Field(runner, "idleBody"), idleOutfit = (Sprite)Field(runner, "idleClothes");
        bool idleFlip = body.flipX;
        Color idleColor = outfit.color;
        Vector3 slotPosition = character.localPosition;
        Require(mask != null && mask.sprite != null
            && Vector2.Distance(mask.transform.localPosition, igloo.localPosition + runner.Door.transform.localPosition) < 1e-4f,
            $"Doorway mask {label} sits on its igloo's door hinge");
        Require(body.sortingOrder == PenguinRunner.DepthOrder(character.localPosition.y) && body.sortingOrder < name.sortingOrder,
            $"Penguin {label} sorts by depth below the names");

        // Ground frame of every igloo: dome base center and canvas pixels per layout unit.
        float pixels = GameLayout.IglooCanvasPixelsPerUnit(iglooSprite);
        Vector2 domeOffset = GameLayout.IglooArtPoint(iglooSprite, IglooGround.DomeCenter);
        var path = runner.Path;
        Require(Vector2.Distance(path[0], character.localPosition) < 1e-4f, $"Run {label} starts at the feet");
        for (int other = 1; other <= count; other++)
        {
            Vector2 dome = (Vector2)root.Find("Igloo " + other).localPosition + domeOffset;
            for (int k = 1; k < path.Count; k++)
                for (int step = 0; step < 8; step++)
                {
                    Vector2 point = Vector2.Lerp(path[k - 1], path[k], step / 8f);
                    Vector2 frame = IglooGround.ToFrame((point - dome) * pixels);
                    if (other != slot)
                        Require(frame.magnitude > 530f, $"Run {label} keeps clear of igloo {other}'s dome ({frame.magnitude:F0} px)");
                    else if (frame.x > IglooGround.TunnelFront + 30f || Mathf.Abs(frame.y) > 140f)
                        Require(frame.magnitude > 600f, $"Run {label} goes around its own igloo, not through it ({frame.magnitude:F0} px)");
                }
        }
        Vector2 end = IglooGround.ToFrame((path[path.Count - 1] - (Vector2)igloo.localPosition - domeOffset) * pixels);
        Require(end.x < IglooGround.TunnelFront - 100f && Mathf.Abs(end.y) < 1f, $"Run {label} ends inside its own tunnel");

        // Simulate the run in fixed steps.
        bool doorOpened = false, maskedBeforeEnd = false;
        float seconds = 0f;
        runner.RunIntoIgloo(1.1f);
        while (runner.IsRunning && seconds < 30f)
        {
            runner.Tick(0.05f);
            seconds += 0.05f;
            doorOpened |= runner.Door.Open > 0.999f;
            if (!runner.IsRunning)
                break;
            Require(body.sprite != outfit.sprite && body.flipX == outfit.flipX && outfit.sortingOrder == body.sortingOrder + 1
                && body.sortingOrder == PenguinRunner.DepthOrder(character.localPosition.y),
                $"Run {label} keeps the outfit on the body and sorts by depth");
            float domeY = igloo.localPosition.y + domeOffset.y;
            if (body.maskInteraction == SpriteMaskInteraction.None && character.localPosition.y > domeY + 0.05f)
                Require(body.sortingOrder < group.sortingOrder, $"Run {label} passes behind its igloo");
            maskedBeforeEnd |= body.maskInteraction == SpriteMaskInteraction.VisibleInsideMask
                && outfit.maskInteraction == SpriteMaskInteraction.VisibleInsideMask && !name.enabled;
        }
        Require(runner.IsInside && Mathf.Abs(seconds - runner.PathLength / 1.1f) < 0.1f,
            $"Run {label} reaches the tunnel in {seconds:F2} s at 1.1 units/s");
        Require(doorOpened && maskedBeforeEnd && !body.enabled && !outfit.enabled && !name.enabled,
            $"Run {label} opens the door, enters through the doorway mask and disappears");
        for (int k = 0; k < 20; k++)
            runner.Tick(0.05f);
        Require(runner.Door.Open < 1e-4f, $"Door {label} closes behind the penguin");

        // Back out to the slot.
        bool doorOpenedOut = false, maskedOut = false, closingWhileClear = true;
        seconds = 0f;
        runner.RunOutOfIgloo(1.1f);
        Require(runner.Current == PenguinRunner.State.RunningOut && runner.WaitingForDoor && !body.enabled
            && !outfit.enabled, $"Run out {label} waits hidden at the tunnel end while its closed door opens");
        bool appearedOpen = true, wasVisible = false;
        float previousOpen = runner.Door.Open, waited = 0f;
        while (runner.IsRunning && seconds < 30f)
        {
            runner.Tick(0.05f);
            // Time spent waiting for the door is not part of the run.
            if (runner.WaitingForDoor)
                waited += 0.05f;
            else
                seconds += 0.05f;
            doorOpenedOut |= runner.Door.Open > 0.999f;
            if (!runner.IsRunning)
                break;
            if (!body.enabled)
                continue;
            if (!wasVisible)
            {
                // It shows up still in the tunnel, behind the doorway mask, once the door is fully open.
                Require(body.maskInteraction == SpriteMaskInteraction.VisibleInsideMask,
                    $"Run out {label} shows up inside the tunnel, behind the doorway mask");
                appearedOpen = runner.Door.Open > 0.999f;
            }
            wasVisible = true;
            Require(body.sprite != outfit.sprite && body.flipX == outfit.flipX && outfit.sortingOrder == body.sortingOrder + 1
                && body.sortingOrder == PenguinRunner.DepthOrder(character.localPosition.y),
                $"Run out {label} keeps the outfit on the body and sorts by depth");
            maskedOut |= body.maskInteraction == SpriteMaskInteraction.VisibleInsideMask;
            // The door only starts closing once the penguin is past the leaf's swing.
            if (runner.Door.Open < previousOpen - 1e-5f)
            {
                Vector2 at = IglooGround.ToFrame(((Vector2)character.localPosition - (Vector2)igloo.localPosition - domeOffset) * pixels);
                closingWhileClear &= at.x > 790f || at.magnitude > 640f;
            }
            previousOpen = runner.Door.Open;
        }
        Require(runner.Current == PenguinRunner.State.Idle && Mathf.Abs(seconds - runner.PathLength / 1.1f) < 0.1f,
            $"Run out {label} reaches its slot in {seconds:F2} s at 1.1 units/s");
        Require(appearedOpen && waited <= PenguinRunner.DoorSeconds + 0.1f,
            $"Run out {label} shows up in the tunnel only once the door is fully open, after {waited:F2} s");
        Require(doorOpenedOut && maskedOut && closingWhileClear && runner.Door.Open < 1e-4f,
            $"Run out {label} opens the door, leaves through the doorway and the door is closed on arrival");
        Require(Vector3.Distance(character.localPosition, slotPosition) < 1e-4f && body.sprite == idleBody
            && outfit.sprite == idleOutfit && body.flipX == idleFlip && outfit.flipX == idleFlip
            && body.maskInteraction == SpriteMaskInteraction.None && body.color == Color.white
            && outfit.color == idleColor && body.enabled && outfit.enabled && name.enabled
            && body.sortingOrder == PenguinRunner.DepthOrder(slotPosition.y),
            $"Run out {label} ends standing on the slot in the idle pose, facing the middle");

        // Turning around midway.
        runner.RunIntoIgloo(1.1f);
        for (int k = 0; k < 30; k++)
            runner.Tick(0.05f);
        runner.RunOutOfIgloo(1.1f);
        Require(runner.Current == PenguinRunner.State.RunningOut, $"Run {label} turns around midway");
        for (int k = 0; k < 600 && (runner.IsRunning || runner.Door.Open > 0f); k++)
            runner.Tick(0.05f);
        Require(runner.Current == PenguinRunner.State.Idle && Vector3.Distance(character.localPosition, slotPosition) < 1e-4f
            && runner.Door.Open < 1e-4f && body.sprite == idleBody, $"Run {label} turned around ends on its slot, door closed");
    }

    // Idle: frames share the still pose's size and pivot, every penguin starts at its own breath phase and
    // speed, clips only change at the end of a clip, every clip turns up, the idle stands aside while the
    // penguin runs and resumes on the slot, and switching it off shows the still pose.
    private static void CheckIdle(Transform root, int count)
    {
        Require(PenguinIdle.FirstFrame(PenguinIdle.Clip.Look) + PenguinIdle.FrameCount(PenguinIdle.Clip.Look)
            == PenguinIdle.TotalFrames, "Idle clip table covers the idle sheets");
        var phases = new System.Collections.Generic.HashSet<int>();
        var seen = new System.Collections.Generic.HashSet<PenguinIdle.Clip>();
        for (int slot = 1; slot <= count; slot++)
        {
            string label = $"{slot}/{count}";
            var character = root.Find("Character " + slot);
            var idle = character.GetComponent<PenguinIdle>();
            var runner = character.GetComponent<PenguinRunner>();
            var body = character.GetComponent<SpriteRenderer>();
            var outfit = character.Find("Outfit").GetComponent<SpriteRenderer>();
            var frames = (Sprite[])Field(idle, "bodyFrames");
            var outfitFrames = (Sprite[])Field(idle, "clothesFrames");
            var rest = (Sprite)Field(idle, "restBody");
            idle.Advance(0f);   // the run checks above moved the penguin without ticking its idle
            Require(idle != null && idle.Playing && idle.IsActive && frames.Length == PenguinIdle.TotalFrames
                && outfitFrames.Length == PenguinIdle.TotalFrames, $"Penguin {label} idles on its slot");
            Require(Array.IndexOf(frames, body.sprite) >= 0 && Array.IndexOf(outfitFrames, outfit.sprite)
                == Array.IndexOf(frames, body.sprite), $"Idle {label} shows matching body and outfit frames");
            if (slot == 1)
                foreach (Sprite frame in frames)
                    Require((frame.bounds.size - rest.bounds.size).magnitude < 1e-4f
                        && (frame.bounds.center - rest.bounds.center).magnitude < 1e-4f,
                        "Idle frames share the still pose's size and pivot");
            Require(idle.Speed >= PenguinIdle.MinSpeed && idle.Speed <= PenguinIdle.MaxSpeed, $"Idle {label} speed in range");
            phases.Add(Mathf.FloorToInt(idle.ClipTime * PenguinIdle.FramesPerSecond));

            // Two minutes in frame steps: the clip only changes when the previous one has run out.
            PenguinIdle.Clip clip = idle.CurrentClip;
            float time = idle.ClipTime;
            for (int k = 0; k < 1800; k++)
            {
                idle.Advance(1f / PenguinIdle.FramesPerSecond);
                if (idle.CurrentClip != clip || idle.ClipTime < time)
                    Require(time + idle.Speed / PenguinIdle.FramesPerSecond >= PenguinIdle.Duration(clip) - 1e-3f,
                        $"Idle {label} changes clip only at the end of one");
                clip = idle.CurrentClip;
                time = idle.ClipTime;
                seen.Add(clip);
                int index = Array.IndexOf(frames, body.sprite);
                Require(index >= PenguinIdle.FirstFrame(clip) && index < PenguinIdle.FirstFrame(clip) + PenguinIdle.FrameCount(clip)
                    && outfit.sprite == outfitFrames[index], $"Idle {label} shows a frame of its current clip");
            }

            // Running takes over the sprites; back on the slot the idle resumes from the top of a breath.
            runner.RunIntoIgloo(1.1f);
            runner.Tick(0.3f);
            idle.Advance(0.1f);
            Require(!idle.IsActive && Array.IndexOf(frames, body.sprite) < 0, $"Idle {label} stands aside while running");
            runner.RunOutOfIgloo(1.1f);
            for (int k = 0; k < 200 && runner.IsRunning; k++)
                runner.Tick(0.05f);
            idle.Advance(0.05f);
            Require(idle.IsActive && idle.CurrentClip == PenguinIdle.Clip.Breathe
                && body.sprite == frames[PenguinIdle.FirstFrame(PenguinIdle.Clip.Breathe)], $"Idle {label} resumes on its slot");

            idle.Playing = false;
            Require(!idle.IsActive && body.sprite == rest, $"Idle {label} off shows the still pose");
            idle.Playing = true;
            idle.Advance(0f);
            Require(idle.IsActive, $"Idle {label} turns back on");
        }
        Require(phases.Count > 1, $"Idle phases differ between the {count} penguins");
        Require(seen.Count == Enum.GetValues(typeof(PenguinIdle.Clip)).Length, "Every idle clip plays");
    }

    // Night action: each igloo's lamp-off pane sits on its window; a visit plays the visited igloo's
    // action (only that one) for the whole stay, the lamp goes out and the igloo shakes, and it all
    // settles back to rest before the visitor comes out.
    private static void CheckAction(GameLayout layout, Transform root, int count)
    {
        Require(layout.Actions.Count == count, "One action per igloo");
        for (int i = 0; i < count; i++)
        {
            var igloo = root.Find("Igloo " + (i + 1));
            var pane = igloo.Find("Window Dark").GetComponent<SpriteRenderer>();
            var light = igloo.Find("Window Light");
            Require(!pane.enabled && layout.Actions[i].Lamp == 1f && !layout.Actions[i].Playing,
                $"Igloo {i + 1}/{count} lamp is lit at rest");
            Require(Vector2.Distance(pane.bounds.center, light.position) < 0.02f,
                $"Igloo {i + 1}/{count} lamp-off pane sits on the window");
        }
        int slot = 0, target = count / 2;
        PenguinRunner runner = layout.Runners[slot];
        var visited = root.Find("Igloo " + (target + 1));
        Vector3 rest = visited.localPosition;
        Require(layout.Visit(slot, target), "Visit starts");
        float step = 1f / 50f, minLamp = 1f, maxTilt = 0f, guestTime = 0f;
        bool played = false;
        for (int frame = 0; frame < 5000 && runner.IsVisiting; frame++)
        {
            runner.Tick(step);
            foreach (IglooAction action in layout.Actions)
                action.Tick(step);
            for (int i = 0; i < count; i++)
                Require(i == target || !layout.Actions[i].Playing, "Only the visited igloo acts");
            if (runner.Current == PenguinRunner.State.Guest)
            {
                guestTime += step;
                played |= layout.Actions[target].Playing;
                minLamp = Mathf.Min(minLamp, layout.Actions[target].Lamp);
                maxTilt = Mathf.Max(maxTilt, Mathf.Abs(Mathf.DeltaAngle(0f, visited.localEulerAngles.z)));
            }
            else if (guestTime > 0f)
                Require(!layout.Actions[target].Playing && visited.localPosition == rest
                    && visited.localRotation == Quaternion.identity && visited.localScale == Vector3.one
                    && layout.Actions[target].Lamp == 1f, "The igloo is back at rest when the visitor comes out");
        }
        Require(played && minLamp < 0.01f && maxTilt > 1f, $"The visited igloo's lamp goes out and it shakes ({maxTilt:F2} deg)");
        Require(guestTime >= GameLayout.VisitMinSeconds - 0.05f && guestTime <= GameLayout.VisitMaxSeconds + 0.05f,
            $"The visitor stays {guestTime:F2} s inside");
        Require(runner.Current == PenguinRunner.State.Inside, "The visitor ends in its own igloo");
    }

    // Heal: each igloo's mint pane sits on its window (shown only while glowing) beside a mint heal light. A
    // heal visit plays the visited igloo's heal (only that one): the lamp dims but never goes out, the window
    // glows mint, the igloo swells with the beats and hops once without tilting, particles rise above the
    // dome (at night the mint light comes on), and it all settles back to rest before the visitor comes out.
    // The curves also end at rest for the shortest and longest stays.
    private static void CheckHeal(GameLayout layout, Transform root, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var igloo = root.Find("Igloo " + (i + 1));
            var pane = igloo.Find("Window Heal").GetComponent<SpriteRenderer>();
            var dark = igloo.Find("Window Dark").GetComponent<SpriteRenderer>();
            var light = igloo.Find("Heal Light").GetComponent<UnityEngine.Rendering.Universal.Light2D>();
            Require(!pane.enabled && !light.enabled && layout.Actions[i].Glow == 0f && layout.Actions[i].VisibleParticles == 0,
                $"Igloo {i + 1}/{count} heal glow is off at rest");
            Require(pane.sortingOrder > dark.sortingOrder && Vector2.Distance(pane.bounds.center, dark.bounds.center) < 0.002f
                && Vector2.Distance(light.transform.position, igloo.Find("Window Light").position) < 0.002f,
                $"Igloo {i + 1}/{count} mint pane and light sit on the window");
            Require(root.Find("Heal Particles " + (i + 1)).childCount > 0, $"Igloo {i + 1}/{count} has heal particles");
        }
        foreach (float stay in new[] { GameLayout.VisitMinSeconds, GameLayout.VisitMaxSeconds })
        {
            IglooAction.SampleHeal(stay - 0.02f, stay, out float lampEnd, out float glowEnd, out Vector2 scaleEnd, out float liftEnd);
            Require(Mathf.Abs(lampEnd - 1f) < 0.01f && glowEnd < 0.05f && (scaleEnd - Vector2.one).magnitude < 0.005f
                && liftEnd == 0f, $"The heal ends at rest for a {stay:F0} s stay");
        }

        int slot = 1, target = (count / 2 + 1) % count;
        PenguinRunner runner = layout.Runners[slot];
        var visited = root.Find("Igloo " + (target + 1));
        var particles = root.Find("Heal Particles " + (target + 1));
        var healLight = visited.Find("Heal Light").GetComponent<UnityEngine.Rendering.Universal.Light2D>();
        Bounds art = visited.GetComponent<SpriteRenderer>().bounds;   // the whole 1254 px canvas
        float apexY = art.min.y + 0.79797f * art.size.y;              // top of the dome
        Vector3 rest = visited.localPosition;
        float hour = layout.TimeOfDay;
        try
        {
            layout.TimeOfDay = 0f;
            Require(layout.Visit(slot, target, IglooAction.Kind.Heal), "Heal visit starts");
            float step = 1f / 50f, minLamp = 2f, maxLamp = 0f, maxGlow = 0f, maxSwell = 1f, maxLift = 0f, maxTilt = 0f;
            float guestTime = 0f, highest = float.NegativeInfinity;
            int maxParticles = 0;
            bool played = false, lit = false;
            for (int frame = 0; frame < 5000 && runner.IsVisiting; frame++)
            {
                runner.Tick(step);
                foreach (IglooAction action in layout.Actions)
                    action.Tick(step);
                layout.TimeOfDay = 0f;   // applies the lights for this frame
                for (int i = 0; i < count; i++)
                    Require(i == target || !layout.Actions[i].Playing, "Only the healed igloo acts");
                IglooAction healed = layout.Actions[target];
                if (runner.Current == PenguinRunner.State.Guest)
                {
                    guestTime += step;
                    played |= healed.Playing && healed.Current == IglooAction.Kind.Heal;
                    minLamp = Mathf.Min(minLamp, healed.Lamp);
                    maxLamp = Mathf.Max(maxLamp, healed.Lamp);
                    maxGlow = Mathf.Max(maxGlow, healed.Glow);
                    maxSwell = Mathf.Max(maxSwell, visited.localScale.y);
                    maxLift = Mathf.Max(maxLift, visited.localPosition.y - rest.y);
                    maxTilt = Mathf.Max(maxTilt, Mathf.Abs(Mathf.DeltaAngle(0f, visited.localEulerAngles.z)));
                    maxParticles = Mathf.Max(maxParticles, healed.VisibleParticles);
                    lit |= healLight.enabled && healLight.intensity > 1f;
                    foreach (SpriteRenderer particle in particles.GetComponentsInChildren<SpriteRenderer>())
                        if (particle.enabled)
                            highest = Mathf.Max(highest, particle.bounds.max.y);
                }
                else if (guestTime > 0f)
                    Require(!healed.Playing && visited.localPosition == rest && visited.localRotation == Quaternion.identity
                        && visited.localScale == Vector3.one && healed.Lamp == 1f && healed.Glow == 0f
                        && healed.VisibleParticles == 0 && !healLight.enabled,
                        "The healed igloo is back at rest when the visitor comes out");
            }
            Require(played, "The visited igloo plays the heal");
            Require(minLamp > 0.25f && minLamp < 0.35f && maxLamp > 1.2f, $"The lamp dims (to {minLamp:F2}) but never goes out, then comes back ({maxLamp:F2})");
            Require(maxGlow > 0.95f && lit, "The window glows mint and the mint light comes on at night");
            Require(maxSwell > 1.03f && maxLift > 0.05f && maxTilt < 0.001f,
                $"The igloo beats and hops without tilting (swell {maxSwell:F3}, lift {maxLift:F3})");
            Require(maxParticles >= 10 && highest > apexY, $"Particles rise above the dome ({maxParticles} at once)");
            Require(runner.Current == PenguinRunner.State.Inside, "The healer ends in its own igloo");
            // Back to its slot: later checks expect it standing there.
            layout.RunOutOfIgloo(slot);
            for (int frame = 0; frame < 5000 && runner.Current != PenguinRunner.State.Idle; frame++)
                runner.Tick(1f / 50f);
            Require(runner.Current == PenguinRunner.State.Idle, "The healer runs back out to its slot");
        }
        finally
        {
            layout.TimeOfDay = hour;
        }
    }

    // Knocked out inside its igloo, a penguin walks out dizzy (dizzy frames only) just past the point its
    // door's leaf reaches (the same walk for every igloo), stops exactly at the fall's start pose, faints
    // there facing the way it walked, its door closes, and it lies there for good: no going in, no visits.
    // Knocked out on its slot, it faints at once.
    private static void CheckKnockOut(GameLayout layout, Transform root, int count)
    {
        int slot = count - 1;
        PenguinRunner runner = layout.Runners[slot];
        var body = root.Find("Character " + (slot + 1)).GetComponent<SpriteRenderer>();
        var name = body.GetComponentInChildren<TextMesh>().transform;
        Vector3 nameRest = name.localPosition;
        var dizzy = new HashSet<Sprite>();
        foreach (Sprite sprite in (Sprite[])Field(runner, "dizzyBody"))
            dizzy.Add(sprite);
        var down = new HashSet<Sprite>();
        foreach (Sprite sprite in (Sprite[])Field(runner, "downBody"))
            down.Add(sprite);
        float step = 1f / 50f;
        runner.RunIntoIgloo(1.1f);
        for (int frame = 0; frame < 2000 && runner.Current != PenguinRunner.State.Inside; frame++)
            runner.Tick(step);
        Require(runner.Current == PenguinRunner.State.Inside, "The penguin went in before the knock-out");
        layout.KnockOut(slot);
        Require(runner.KnockedOut && runner.Current == PenguinRunner.State.Inside, "Knocked out inside, it stays in");
        layout.RunOutOfIgloo(slot);
        bool facing = false;
        for (int frame = 0; frame < 5000 && runner.Current == PenguinRunner.State.RunningOut; frame++)
        {
            facing = body.flipX;
            runner.Tick(step);
            if (runner.Current == PenguinRunner.State.RunningOut && body.enabled)
                Require(dizzy.Contains(body.sprite), "Walking out dizzy shows dizzy frames only");
        }
        Require(runner.Current == PenguinRunner.State.Fainting, "It faints after walking out");
        float phase = Mathf.Repeat((float)Field(runner, "stride") * (float)Field(runner, "dizzyScale")
            / PenguinRunner.DizzyLoopDistance, 1f);
        Require(Mathf.Abs(phase - PenguinRunner.FaintStartLoop) < 0.01f,
            $"The dizzy walk ends at the fall's start pose (loop {phase:F3})");
        Require(Mathf.Abs(runner.PathLength - runner.FaintDistance - (layout.Runners[0].PathLength
                - layout.Runners[0].FaintDistance)) < 1e-3f && runner.FaintDistance > 0f,
            "Every knocked-out penguin walks the same distance out of its igloo");
        Require(Mathf.Abs(runner.Travelled - runner.FaintDistance) < 1e-4f
            && Vector2.Distance(body.transform.localPosition, runner.FaintPosition) < 1e-4f
            && body.flipX == facing, "It faints where its walk stopped, facing the way it walked");
        for (float t = 0f; t < PenguinRunner.FaintSeconds + 0.5f; t += step)
            runner.Tick(step);
        Require(runner.Current == PenguinRunner.State.Down && down.Contains(body.sprite)
            && name.localPosition.y < nameRest.y, "It lies down for good, its name lower");
        Require(runner.Door.Open == 0f, "Its door has closed behind it");
        runner.RunIntoIgloo(1.1f);
        layout.RunOutOfIgloo(slot);
        Require(runner.Current == PenguinRunner.State.Down && !layout.Visit(slot, 0),
            "A knocked-out penguin neither goes in nor visits");
        PenguinRunner standing = layout.Runners[1];
        Require(standing.Current == PenguinRunner.State.Idle, "Another penguin stands on its slot");
        layout.KnockOut(1);
        Require(standing.Current == PenguinRunner.State.Fainting, "Knocked out on its slot, it faints at once");
    }

    // The inspector Knock Out field leaves only its current slot knocked out: dragging the slider passes
    // through other slots, which come back.
    private static void CheckKnockOutPreview(GameLayout layout, SerializedObject serialized)
    {
        var field = serialized.FindProperty("knockOut");
        int original = field.intValue;
        var apply = typeof(GameLayout).GetMethod("ApplyKnockOutSetting",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        System.Action<int> set = value =>
        {
            serialized.Update();
            field.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            apply.Invoke(layout, null);
        };
        try
        {
            set(0);
            layout.Rebuild();
            foreach (int value in new[] { 1, 3, layout.Runners.Count, 2 })
                set(value);
            for (int i = 0; i < layout.Runners.Count; i++)
                Require(layout.Runners[i].KnockedOut == (i == 1)
                    && (i == 1 || layout.Runners[i].Current == PenguinRunner.State.Idle),
                    "Only the slot the Knock Out field ends on is knocked out");
            set(0);
            foreach (PenguinRunner runner in layout.Runners)
                Require(!runner.KnockedOut && runner.Current == PenguinRunner.State.Idle,
                    "Knock Out 0 brings the previewed penguin back");
        }
        finally
        {
            serialized.Update();
            field.intValue = original;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(target);

    private static bool Approximately(Matrix4x4 a, Matrix4x4 b)
    {
        for (int k = 0; k < 16; k++)
            if (Mathf.Abs(a[k] - b[k]) > 1e-5f)
                return false;
        return true;
    }

    private static void CheckDayNightCycle()
    {
        DayNightCycle.Evaluate(0f, out Color midnight, out float midnightIntensity);
        DayNightCycle.Evaluate(DayNightCycle.Hours, out Color wrapped, out float wrappedIntensity);
        DayNightCycle.Evaluate(DayNightCycle.Noon, out _, out float noonIntensity);
        Require(midnight == wrapped && Mathf.Approximately(midnightIntensity, wrappedIntensity),
            "Day-night cycle wraps smoothly at midnight");
        Require(Mathf.Approximately(DayNightCycle.Darkness(noonIntensity), 0f)
            && Mathf.Approximately(DayNightCycle.Darkness(midnightIntensity), 1f),
            "Noon is full daylight and midnight is full night");
        float previous = midnightIntensity;
        for (int step = 1; step <= 480; step++)
        {
            DayNightCycle.Evaluate(step * DayNightCycle.Hours / 480f, out _, out float intensity);
            Require(Mathf.Abs(intensity - previous) < 0.02f, "Day-night light changes without jumps");
            previous = intensity;
        }
    }

    private static Vector2[] DenseBoundary()
    {
        var points = new Vector2[16384];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = i * Mathf.PI * 2 / points.Length;
            points[i] = GameLayout.LayoutCenter + new Vector2(3.6f * Mathf.Cos(angle), 2f * Mathf.Sin(angle));
        }
        return points;
    }

    private static Vector2[] Silhouette(Transform root, Transform igloo)
    {
        Vector2[] outline = GameLayout.IglooOutline(igloo.GetComponent<SpriteRenderer>().sprite);
        for (int i = 0; i < outline.Length; i++)
            outline[i] = root.InverseTransformPoint(igloo.TransformPoint(outline[i]));
        return outline;
    }

    // Separating-axis test for two convex polygons, with a required clearance.
    private static bool Separated(Vector2[] a, Vector2[] b, float margin)
    {
        foreach (Vector2[] polygon in new[] { a, b })
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity;
                float bMin = float.PositiveInfinity, bMax = float.NegativeInfinity;
                foreach (Vector2 point in a)
                {
                    aMin = Mathf.Min(aMin, Vector2.Dot(point, axis));
                    aMax = Mathf.Max(aMax, Vector2.Dot(point, axis));
                }
                foreach (Vector2 point in b)
                {
                    bMin = Mathf.Min(bMin, Vector2.Dot(point, axis));
                    bMax = Mathf.Max(bMax, Vector2.Dot(point, axis));
                }
                if (aMax + margin < bMin || bMax + margin < aMin)
                    return true;
            }
        return false;
    }

    // Degrees between a point's direction from the layout center and the expected clockwise-from-top angle.
    private static float AngleError(Vector2 point, float expected)
    {
        Vector2 relative = point - GameLayout.LayoutCenter;
        float actual = Mathf.Atan2(relative.x, relative.y) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(actual, expected));
    }

    // Brute-force shortest distance between two polygon outlines, sampled densely along every edge;
    // independent of the Minkowski-sum shortcut used for placement.
    private static float DenseDistance(Vector2[] a, Vector2[] b)
    {
        var samplesA = DenseOutline(a);
        var samplesB = DenseOutline(b);
        float nearest = float.PositiveInfinity;
        foreach (Vector2 p in samplesA)
            foreach (Vector2 q in samplesB)
                nearest = Mathf.Min(nearest, (p - q).sqrMagnitude);
        return Mathf.Sqrt(nearest);
    }

    private static System.Collections.Generic.List<Vector2> DenseOutline(Vector2[] polygon)
    {
        var points = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < polygon.Length; i++)
            for (int step = 0; step < 12; step++)
                points.Add(Vector2.Lerp(polygon[i], polygon[(i + 1) % polygon.Length], step / 12f));
        return points;
    }

    private static bool Inside(Vector2 point)
    {
        Vector2 relative = point - GameLayout.LayoutCenter;
        return relative.x * relative.x / (3.6f * 3.6f) + relative.y * relative.y / 4f < 1;
    }

    private static float Nearest(Vector2 point, Vector2[] boundary)
    {
        // Coarse scan, then a full-resolution scan around the best coarse sample.
        int best = 0;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < boundary.Length; i += 16)
        {
            float distance = (boundary[i] - point).sqrMagnitude;
            if (distance < nearest)
            {
                nearest = distance;
                best = i;
            }
        }
        for (int offset = -64; offset <= 64; offset++)
        {
            int i = (best + offset + boundary.Length) % boundary.Length;
            nearest = Mathf.Min(nearest, (boundary[i] - point).sqrMagnitude);
        }
        return Mathf.Sqrt(nearest);
    }

    private static void CheckHoleClearance(Vector2 character)
    {
        Vector2 fromHole = character - GameLayout.LayoutCenter;
        float closestX = Mathf.Max(Mathf.Abs(fromHole.x) - GameLayout.CharacterSize.x / 2f, 0);
        float closestY = Mathf.Max(Mathf.Abs(fromHole.y) - GameLayout.CharacterHeight / 2f, 0);
        Require(closestX * closestX / (1.1f * 1.1f) + closestY * closestY / (0.85f * 0.85f) > 1f,
            "Character body stays outside the clear area around the hole");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
