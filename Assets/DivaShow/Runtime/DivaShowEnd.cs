using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    // 结束动画（慢动作 + 扬鼻长鸣 + 彩带烟花）→ 本局高光回放 → 结算卡片和排行榜
    public partial class DivaShowDirector
    {
        // ------------------------------------------------------------------ finish
        readonly float[] fireworkTimes = { .5f, 1.1f, 1.6f, 2.3f, 3.0f };
        int fireworkNext;

        void EnterFinish()
        {
            finishedTime = Time.time - runStart;
            replay.Mark(finishedTime, "finish", 50, timeUp ? "timeup" : "finish");
            if (waterGun) waterGun.StopSpray();
            if (boosters) boosters.SetBoost(1);
            sound.Play("fanfare");
            Banner(DivaText.T(timeUp ? "timeup" : "finish"), DivaText.F("finish.sub", DivaUi.Clock(finishedTime), TotalScore), Accent, 4.2f, null);
            fx.Confetti(ElephantCenter + Vector3.up * ElephantSize * .3f, new[] { Primary, Secondary, Accent, Color.white, new Color(1, .85f, .3f) });
            fireworkNext = 0;
            finishAngle = BestAngle(ElephantSize * 1.4f, ElephantSize * .15f, 60);
            if (pipCamera) SetPip(false);
        }

        void UpdateFinish()
        {
            float t = PhaseTime;
            Time.timeScale = t < 1.3f ? .35f : 1;
            if (t > .8f && boosters) boosters.SetBoost(-1);
            while (fireworkNext < fireworkTimes.Length && t >= fireworkTimes[fireworkNext])
            {
                // 烟花放在镜头看过去、大象后面远处的天上，才在画面里
                var view = mainCamera ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized : Forward;
                var right = Vector3.Cross(Vector3.up, view);
                float side = fireworkNext % 2 == 0 ? -1 : 1;
                var pos = elephant.position + view * (20 + fireworkNext * 2) + right * side * (4 + fireworkNext * 2) + Vector3.up * (6 + fireworkNext % 3 * 1.5f);
                var colors = new[] { Primary, Accent, Secondary, new Color(1, .85f, .3f) };
                fx.Firework(pos, colors[fireworkNext % colors.Length] * 1.5f);
                sound.Play("pop", .5f);
                fireworkNext++;
            }
            if (t > 5.2f || (skipEdge || ultEdge) && t > 1)
            {
                Time.timeScale = 1;
                Enter(replay.frames.Count > Mathf.CeilToInt(DivaReplay.Hz * 2) ? ShowPhase.Highlight : ShowPhase.Results);
            }
        }

        // 扬鼻长鸣：在步态动画之后叠上 trumpet 片段（只改骨骼，大象位置不动）
        void PoseFinish()
        {
            float t = PhaseTime;
            if (trumpetClip && animator)
            {
                var a = animator.transform;
                Vector3 lp = a.localPosition; Quaternion lr = a.localRotation;
                trumpetClip.SampleAnimation(animator.gameObject, Mathf.Min(t * .9f, trumpetClip.length - .02f));
                a.localPosition = lp; a.localRotation = lr;
                if (locomotion.animationMotionRoot)
                    locomotion.animationMotionRoot.SetLocalPositionAndRotation(motionRootPosition, motionRootRotation);
            }
            var fwd = Forward; var right = Vector3.Cross(Vector3.up, fwd);
            float size = ElephantSize;
            var center = ElephantCenter;
            float angle = finishAngle + Mathf.Lerp(-25, 25, DivaUi.Smooth(t / 5f));
            var dir = Quaternion.AngleAxis(angle, Vector3.up) * fwd;
            var pos = center + dir * size * Mathf.Lerp(1.1f, 1.6f, DivaUi.Smooth(t / 4f)) + Vector3.up * size * Mathf.Lerp(.02f, .22f, DivaUi.Smooth(t / 4f));
            SetCamera(pos, center + Vector3.up * size * .1f, 44);
        }

        float finishAngle, resultsAngle;

        // ------------------------------------------------------------------ highlight replay
        float hlStart, hlEnd, hlKey, hlTime;
        string hlLabel;
        List<DivaReplay.Event> hlHits = new List<DivaReplay.Event>();
        int hlNext;
        TextMeshProUGUI hlTitle, hlSub, hlSkip, hlRec;

        void BuildHighlightUi()
        {
            DivaUi.Image(highlightRoot, "Bar Top", new Vector2(.5f, 1), Vector2.zero, new Vector2(4000, 120), Color.black).rectTransform.pivot = new Vector2(.5f, 1);
            DivaUi.Image(highlightRoot, "Bar Bottom", new Vector2(.5f, 0), Vector2.zero, new Vector2(4000, 120), Color.black).rectTransform.pivot = new Vector2(.5f, 0);
            hlTitle = DivaUi.Text(highlightRoot, "Title", new Vector2(0, 1), new Vector2(64, -10), new Vector2(1000, 100), 84, Color.white, TextAlignmentOptions.Left);
            DivaUi.Localize(hlTitle, () => DivaText.T("highlight"));
            hlSub = DivaUi.Text(highlightRoot, "Moment", new Vector2(0, 1), new Vector2(68, -134), new Vector2(1400, 56), 44, Color.white, TextAlignmentOptions.Left);
            hlRec = DivaUi.Text(highlightRoot, "Replay", new Vector2(1, 1), new Vector2(-60, -30), new Vector2(400, 60), 40, new Color(1, .3f, .35f), TextAlignmentOptions.Right, false);
            hlSkip = DivaUi.Text(highlightRoot, "Skip", new Vector2(.5f, 0), new Vector2(0, 38), new Vector2(1400, 44), 28, new Color(1, 1, 1, .7f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(hlSkip, () => DivaText.T("highlight.skip"));
        }

        void EnterHighlight()
        {
            var best = replay.Best(highlightSeconds);
            hlStart = best.start; hlEnd = best.end; hlKey = best.key; hlLabel = best.label;
            hlTime = hlStart;
            // 回放时打靶不计分：暂停 Eva 的计分组件，靶子先立起来，到时间再打倒
            if (game) game.enabled = false;
            hlHits = replay.events.Where(e => e.kind == "hit" && e.target && e.t >= hlStart && e.t <= hlEnd).OrderBy(e => e.t).ToList();
            foreach (var e in hlHits) e.target.ResetTarget();
            hlNext = 0;
            DivaUi.Glow(hlTitle, DivaUi.WithAlpha(Accent, .95f), .7f, .4f, .1f);
            hlSub.text = (string.IsNullOrEmpty(hlLabel) ? "" : DivaText.T(hlLabel) + "   ·   ") + SkinName;
            sound.Play("whoosh", .7f);
            hlShot = -1;
        }

        void UpdateHighlight()
        {
            float rate = Mathf.Abs(hlTime - hlKey) < .7f ? .4f : 1;
            Time.timeScale = rate;
            hlTime += Time.unscaledDeltaTime * rate;
            while (hlNext < hlHits.Count && hlHits[hlNext].t <= hlTime)
            {
                var target = hlHits[hlNext].target;
                if (target) { target.ResetTarget(); target.TryHit(); fx.Flash(target.AimPoint, Accent, 2.4f, .3f); sound.Play("hit", .7f); }
                hlNext++;
            }
            hlRec.text = (Mathf.Repeat(Time.unscaledTime, 1) < .6f ? "● " : "   ") + DivaText.T("replay");
            hlTitle.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-400, 64, DivaUi.EaseOut(PhaseTime / .4f)), -10);
            if (hlTime >= hlEnd || (skipEdge || ultEdge) && PhaseTime > .6f)
            {
                EndHighlight();
                Enter(ShowPhase.Results);
            }
        }

        void EndHighlight()
        {
            Time.timeScale = 1;
            if (game) game.enabled = true;
            if (waterGun) waterGun.spraying = false;
            if (bubbles) bubbles.blowing = false;
            if (boosters) boosters.SetBoost(-1);
        }

        int hlShot;
        Vector3 hlShotSide;

        void PoseHighlight()
        {
            var f = replay.Apply(hlTime);
            if (waterGun) { waterGun.spraying = f.spray; if (f.spray) waterGun.jetSpeed = f.jetSpeed; }
            if (bubbles) bubbles.blowing = f.blow;
            if (boosters) boosters.SetBoost(Mathf.Clamp01(f.boost));

            float span = Mathf.Max(.1f, hlEnd - hlStart), u = (hlTime - hlStart) / span;
            bool key = Mathf.Abs(hlTime - hlKey) < 1.1f && !string.IsNullOrEmpty(hlLabel);
            int shot = key ? 3 : Mathf.Clamp((int)(u * 3), 0, 2);
            if (shot != hlShot)
            {
                hlShot = shot;
                var r0 = Vector3.Cross(Vector3.up, Forward);
                float left = ClearDistance(ElephantCenter, ElephantCenter - r0 * ElephantSize * 1.3f), rightClear = ClearDistance(ElephantCenter, ElephantCenter + r0 * ElephantSize * 1.3f);
                hlShotSide = rightClear >= left ? Vector3.right : Vector3.left;
            }
            var fwd = Forward; var right = Vector3.Cross(Vector3.up, fwd) * (hlShotSide.x < 0 ? -1 : 1);
            float size = ElephantSize;
            var center = ElephantCenter;
            switch (shot)
            {
                case 0: // 侧面低角度跟拍
                    SetCamera(center + right * size * 1.3f + fwd * size * .4f + Vector3.down * size * .05f, center + fwd * size * .2f, 42); break;
                case 1: // 正面慢慢环绕
                    var dir = Quaternion.AngleAxis(Mathf.Lerp(-40, 40, u * 3 - 1), Vector3.up) * fwd;
                    SetCamera(center + dir * size * 1.4f + Vector3.up * size * .15f, center, 40); break;
                case 2: // 后上方摇臂
                    SetCamera(center - fwd * size * 1.4f + right * size * .4f + Vector3.up * size * .7f, center + fwd * size * .8f, 50); break;
                default: // 关键一击：从被打中的靶子那边看回大象，或者鼻子特写
                    // 选离大象最远的那个被打中的靶子，从靶子后面隔着它看回大象
                    var hit = hlHits.Where(e => Mathf.Abs(e.t - hlKey) < 1.2f && e.target)
                        .OrderByDescending(e => Vector3.Distance(e.target.AimPoint, center)).Select(e => e.target).FirstOrDefault();
                    if (hit && Vector3.Distance(hit.AimPoint, center) > elephantRadius * 1.6f)
                    {
                        var to = (center - hit.AimPoint); to.y = 0; to.Normalize();
                        var side = Vector3.Cross(Vector3.up, to);
                        var pos = hit.AimPoint - to * 3f + side * 1.6f + Vector3.up * 1.4f;
                        SetCamera(pos, Vector3.Lerp(center, hit.AimPoint, .3f), 44);
                    }
                    else
                    {
                        // 没有远处的靶子：斜前方中景，看得到鼻子和泡泡
                        SetCamera(center + fwd * size * .9f + right * size * .9f + Vector3.up * size * .1f, center + fwd * size * .2f, 44);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ results + leaderboard
        class Card { public DivaSlant plate; public TextMeshProUGUI role, title; public readonly List<(TextMeshProUGUI label, TextMeshProUGUI value)> rows = new List<(TextMeshProUGUI, TextMeshProUGUI)>(); }
        readonly List<Card> cards = new List<Card>();
        readonly List<(DivaSlant plate, TextMeshProUGUI rank, TextMeshProUGUI team, TextMeshProUGUI score, TextMeshProUGUI time, TextMeshProUGUI hits)> boardRows =
            new List<(DivaSlant, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)>();
        TextMeshProUGUI resTeam, resSummary, resScore, resRank;
        DivaRunRecord record;
        List<DivaRunRecord> board = new List<DivaRunRecord>();
        int rank;
        const int BoardRows = 8;
        DivaSlant boardPlate;

        void BuildResultsUi()
        {
            DivaUi.Image(resultsRoot, "Dim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000), new Color(.03f, .02f, .08f, .55f));
            var tl = new Vector2(0, 1);
            var title = DivaUi.Text(resultsRoot, "Title", tl, new Vector2(64, -34), new Vector2(900, 110), 96, Color.white, TextAlignmentOptions.Left);
            DivaUi.Localize(title, () => DivaText.T("results"));
            DivaUi.Glow(title, new Color(1, .3f, .7f, .9f));
            resTeam = DivaUi.Text(resultsRoot, "Team", tl, new Vector2(68, -146), new Vector2(1000, 70), 60, Color.white, TextAlignmentOptions.Left);
            resSummary = DivaUi.Text(resultsRoot, "Summary", tl, new Vector2(70, -218), new Vector2(1000, 50), 34, new Color(1, 1, 1, .85f), TextAlignmentOptions.Left, false);
            resScore = DivaUi.Text(resultsRoot, "Score", tl, new Vector2(64, -262), new Vector2(800, 150), 150, Color.white, TextAlignmentOptions.Left);
            resRank = DivaUi.Text(resultsRoot, "Rank", tl, new Vector2(70, -414), new Vector2(900, 60), 46, Color.white, TextAlignmentOptions.Left);

            for (int i = 0; i < 3; i++)
            {
                var c = new Card();
                var a = new Vector2(0, 0);
                c.plate = DivaUi.Panel(resultsRoot, "Card " + i, a, new Vector2(64 + i * 372, 160), new Vector2(350, 360), Ink, DivaUi.WithAlpha(Ink, .4f), 20);
                c.plate.rectTransform.pivot = Vector2.zero;
                c.plate.stripeWidth = 8;
                c.role = DivaUi.Text(c.plate.transform, "Role", new Vector2(0, 1), new Vector2(26, -14), new Vector2(320, 40), 30, Color.white, TextAlignmentOptions.Left);
                c.title = DivaUi.Text(c.plate.transform, "Title", new Vector2(0, 1), new Vector2(24, -54), new Vector2(320, 64), 54, Color.white, TextAlignmentOptions.Left);
                for (int r = 0; r < 5; r++)
                {
                    var label = DivaUi.Text(c.plate.transform, "Stat " + r, new Vector2(0, 1), new Vector2(26, -136 - r * 42), new Vector2(200, 40), 26, new Color(1, 1, 1, .75f), TextAlignmentOptions.Left, false);
                    var value = DivaUi.Text(c.plate.transform, "Value " + r, new Vector2(1, 1), new Vector2(-24, -136 - r * 42), new Vector2(200, 40), 32, Color.white, TextAlignmentOptions.Right);
                    c.rows.Add((label, value));
                }
                cards.Add(c);
            }

            var right = new Vector2(1, .5f);
            var plate = boardPlate = DivaUi.Panel(resultsRoot, "Board", new Vector2(1, 1), new Vector2(-50, -100), new Vector2(800, 120 + 64 * (BoardRows + 1)), Ink, DivaUi.WithAlpha(Ink, .5f), 24);
            var head = DivaUi.Text(plate.transform, "Head", new Vector2(0, 1), new Vector2(34, -18), new Vector2(600, 60), 52, Color.white, TextAlignmentOptions.Left);
            DivaUi.Localize(head, () => DivaText.T("board"));
            void Col(string key, float x, float w, TextAlignmentOptions align)
            {
                var t = DivaUi.Text(plate.transform, "Col " + key, new Vector2(0, 1), new Vector2(x, -84), new Vector2(w, 34), 24, new Color(1, 1, 1, .6f), align, false);
                DivaUi.Localize(t, () => DivaText.T(key));
            }
            Col("col.rank", 34, 70, TextAlignmentOptions.Left); Col("col.team", 110, 320, TextAlignmentOptions.Left);
            Col("col.score", 430, 120, TextAlignmentOptions.Right); Col("col.time", 560, 110, TextAlignmentOptions.Right); Col("col.hits", 680, 90, TextAlignmentOptions.Right);
            for (int i = 0; i < BoardRows + 1; i++)
            {
                float y = -124 - i * 64;
                var row = DivaUi.Panel(plate.transform, "Row " + i, new Vector2(.5f, 1), new Vector2(0, y), new Vector2(760, 56), DivaUi.WithAlpha(Color.white, .06f), DivaUi.WithAlpha(Color.white, .02f), 12);
                row.rectTransform.pivot = new Vector2(.5f, 1);
                TextMeshProUGUI Cell(float x, float w, float size, TextAlignmentOptions align) =>
                    DivaUi.Text(plate.transform, "Cell", new Vector2(0, 1), new Vector2(x, y - 8), new Vector2(w, 44), size, Color.white, align, false);
                boardRows.Add((row, Cell(34, 70, 32, TextAlignmentOptions.Left), Cell(110, 320, 32, TextAlignmentOptions.Left), Cell(430, 120, 34, TextAlignmentOptions.Right),
                               Cell(560, 110, 30, TextAlignmentOptions.Right), Cell(680, 90, 30, TextAlignmentOptions.Right)));
            }
            DivaUi.Button(resultsRoot, "Again", new Vector2(1, 0), new Vector2(-50, 60), new Vector2(440, 104), () => DivaText.T("again"),
                          new Color(1, .33f, .66f, .95f), Color.white, 58, PlayAgain, 26);
            var hint = DivaUi.Text(resultsRoot, "Again Hint", new Vector2(1, 0), new Vector2(-60, 172), new Vector2(600, 36), 26, new Color(1, 1, 1, .7f), TextAlignmentOptions.Right, false);
            DivaUi.Localize(hint, () => DivaText.T("again.hint"));
            DivaText.Changed += FillResults;
        }

        void EnterResults()
        {
            int shots = laser ? laser.Shots - stats.shots0 : 0;
            record = new DivaRunRecord
            {
                teamAdjective = Random.Range(0, DivaText.TeamAdjectives.Length), teamNoun = Random.Range(0, DivaText.TeamNouns.Length),
                score = TotalScore, hits = stats.hits, shots = shots, tasks = game ? game.tasks.Count(t => t.Done) : 0, bestCombo = stats.bestCombo,
                seconds = finishedTime, distance = stats.distance, topSpeed = stats.topSpeed, skin = skins && skins.CurrentSkin != null ? skins.CurrentSkin.id : "",
                date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), finished = !timeUp, players = singlePlayer ? 1 : 3,
            };
            (board, rank) = DivaLeaderboard.Add(record);
            resultsAngle = BestAngle(ElephantSize * 1.6f, ElephantSize * .25f, 60);
            sound.Play("confirm");
            FillResults();
        }

        void FillResults()
        {
            if (record == null || !resTeam) return;
            resTeam.text = DivaText.Team(record.teamAdjective, record.teamNoun);
            DivaUi.Glow(resTeam, DivaUi.WithAlpha(Primary, .9f), .6f, .3f, .1f);
            resSummary.text = SkinName + "   ·   " + DivaUi.Clock(record.seconds) + "   ·   " +
                              DivaText.T("stat.tasks") + " " + record.tasks + "/" + (game ? game.tasks.Length : 4);
            resScore.text = record.score + "<size=40%> " + (DivaText.Chinese ? "分" : "PTS") + "</size>";
            DivaUi.Glow(resScore, DivaUi.WithAlpha(Accent, .9f), .7f, .4f, .1f);
            resRank.text = DivaText.F("rank", rank) + (rank == 1 ? "    <color=#FFD94A>" + DivaText.T("best") + "</color>" : "");

            float run = locomotion ? locomotion.runSpeed : 4.5f;
            int ownHits = stats.hits - stats.ultHits, shots = Mathf.Max(record.shots, 0);
            string acc = shots > 0 ? Mathf.RoundToInt(100f * ownHits / shots) + "%" : "-";
            string dist = DivaText.F("unit.m", stats.distance), top = DivaText.F("unit.ms", stats.topSpeed);
            string hitShot = stats.hits + " / " + shots, turn = DivaText.F("unit.deg", stats.turned), bub = DivaText.F("unit.s", stats.bubbleTime);
            if (singlePlayer)
            {
                string titleKey = ownHits >= 6 && shots > 0 && ownHits * 2 >= shots ? "title.sharp" : stats.topSpeed >= run * .9f ? "title.speed"
                    : stats.bubbleTime > 6 ? "title.bubble" : stats.turned > 720 ? "title.driver" : "title.explorer";
                SetCard(0, "role.solo", titleKey, Primary, ("stat.distance", dist), ("stat.top", top), ("stat.hits", hitShot), ("stat.combo", "x" + stats.bestCombo), ("stat.bubbles", bub));
                cards[0].plate.rectTransform.sizeDelta = new Vector2(560, 360);
                cards[1].plate.gameObject.SetActive(false); cards[2].plate.gameObject.SetActive(false);
            }
            else
            {
                cards[0].plate.rectTransform.sizeDelta = new Vector2(350, 360);
                SetCard(0, "role.p1", "title.speed", Primary, ("stat.distance", dist), ("stat.top", top));
                SetCard(1, "role.p2", "title.driver", Secondary, ("stat.turn", turn), ("stat.tasks", record.tasks + "/" + (game ? game.tasks.Length : 4)));
                SetCard(2, "role.p3", ownHits >= 3 || stats.bubbleTime < 4 ? "title.sharp" : "title.bubble", Accent,
                        ("stat.hits", hitShot), ("stat.accuracy", acc), ("stat.combo", "x" + stats.bestCombo), ("stat.bubbles", bub));
            }

            int current = rank - 1;
            int shown = Mathf.Min(board.Count, BoardRows) + (current >= BoardRows ? 1 : 0);
            boardPlate.rectTransform.sizeDelta = new Vector2(800, 140 + 64 * shown);
            for (int i = 0; i < boardRows.Count; i++)
            {
                int index = i < BoardRows ? i : current;
                bool visible = i < BoardRows ? i < board.Count : current >= BoardRows;
                var row = boardRows[i];
                foreach (var g in new Graphic[] { row.plate, row.rank, row.team, row.score, row.time, row.hits }) g.gameObject.SetActive(visible);
                if (!visible) continue;
                var r = board[index];
                bool mine = index == current;
                row.plate.Set(mine ? DivaUi.WithAlpha(Accent * .8f, .55f) : DivaUi.WithAlpha(Color.white, i % 2 == 0 ? .07f : .03f),
                              mine ? DivaUi.WithAlpha(Accent * .5f, .35f) : DivaUi.WithAlpha(Color.white, .02f));
                row.rank.text = (index + 1).ToString();
                row.team.text = DivaText.Team(r.teamAdjective, r.teamNoun) + (r.players == 3 ? " <size=70%><alpha=#88>3P" : "");
                row.score.text = r.score.ToString();
                row.time.text = DivaUi.Clock(r.seconds);
                row.hits.text = r.hits.ToString();
                row.rank.color = index == 0 ? new Color(1, .85f, .3f) : Color.white;
            }
        }

        void SetCard(int i, string role, string title, Color color, params (string label, string value)[] rows)
        {
            var c = cards[i];
            c.plate.gameObject.SetActive(true);
            c.plate.stripe = color; c.plate.SetVerticesDirty();
            c.role.text = DivaText.T(role); c.role.color = color;
            c.title.text = DivaText.T(title);
            DivaUi.Glow(c.title, DivaUi.WithAlpha(color, .85f), .6f, .3f, .1f);
            for (int r = 0; r < c.rows.Count; r++)
            {
                bool on = r < rows.Length;
                c.rows[r].label.gameObject.SetActive(on); c.rows[r].value.gameObject.SetActive(on);
                if (on) { c.rows[r].label.text = DivaText.T(rows[r].label); c.rows[r].value.text = rows[r].value; }
            }
        }

        void UpdateResults()
        {
            float t = PhaseTime;
            for (int i = 0; i < cards.Count; i++)
                cards[i].plate.rectTransform.localScale = Vector3.one * DivaUi.EaseOutBack((t - .15f * i) / .4f);
            resScore.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1, DivaUi.EaseOut(t / .4f));
            if ((confirmEdge || ultEdge) && t > 1) PlayAgain();
        }

        void PlayAgain()
        {
            if (Phase != ShowPhase.Results) return;
            sound.Play("confirm");
            Enter(ShowPhase.Select);
        }

        void CameraResults()
        {
            var center = ElephantCenter; float size = ElephantSize;
            var dir = Quaternion.AngleAxis(resultsAngle + 25 * Mathf.Sin(PhaseTime * .3f), Vector3.up) * Forward;
            var pos = center + dir * size * 1.6f + Vector3.up * size * .25f;
            var right = Vector3.Cross(Vector3.up, (center - pos).normalized);
            SetCamera(pos, center - right * size * .3f, 40);
        }
    }
}
