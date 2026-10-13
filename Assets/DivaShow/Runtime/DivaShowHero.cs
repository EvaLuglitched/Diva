using System.Collections;
using TMPro;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 冲线的"英雄时刻"卡（参考守望先锋的 Play of the Game；VR 的 DivaVRCelebration 是同一张卡的座舱版）：
    /// 0.0 s 慢动作（Finish 阶段本来就有）+ 低音轰 + 象鸣；0.6 s 卡片砸进来"PLAY OF THE RACE"；1.0 s 用时 · 任务 · 涂装；
    /// 1.3 s 分数从 0 嘀嘀嘀涨到最终分；2.7 s 欢呼；3.2 s 圈速奖牌图章（金 / 银 / 铜；超时就"冠军"）。进高光回放时消失。
    /// </summary>
    public partial class DivaShowDirector
    {
        RectTransform heroRoot, heroCard;
        CanvasGroup heroGroup;
        TextMeshProUGUI heroTitle, heroSub, heroScore, heroStamp;
        float heroPop = -1, heroStampAt = -1;
        Coroutine heroSequence;

        void BuildHero()
        {
            heroCard = DivaUi.Rect(heroRoot, "Hero Card", new Vector2(.5f, .5f), new Vector2(0, 70), new Vector2(1360, 480));
            heroGroup = heroCard.gameObject.AddComponent<CanvasGroup>(); heroGroup.alpha = 0; heroGroup.blocksRaycasts = false;
            var ink = new Color(.06f, .05f, .12f);
            var plate = DivaUi.Panel(heroCard, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1360, 480), ink, new Color(.55f, .5f, .7f, 1), 60);
            plate.stripe = new Color(1f, .45f, .8f); plate.stripeWidth = 26;
            DivaUi.Panel(heroCard, "Band", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), new Color(1f, .35f, .72f), new Color(.85f, .2f, .6f), 40);
            heroTitle = DivaUi.Text(heroCard, "Title", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), 64, Color.white);
            heroSub = DivaUi.Text(heroCard, "Sub", new Vector2(.5f, 1), new Vector2(0, -168), new Vector2(1200, 56), 38, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            // 分数在左下、图章在右下，互不压字
            heroScore = DivaUi.Text(heroCard, "Score", new Vector2(0, 0), new Vector2(80, 50), new Vector2(820, 150), 140, Color.white, TextAlignmentOptions.Left);
            heroStamp = DivaUi.Text(heroCard, "Stamp", new Vector2(1, 0), new Vector2(-80, 70), new Vector2(460, 110), 76, GoldColour, TextAlignmentOptions.Right);
            DivaUi.Glow(heroScore, new Color(1f, .85f, .3f, .95f));
            DivaUi.Glow(heroTitle, new Color(1, .3f, .7f, .8f));
            DivaUi.Glow(heroStamp, new Color(1f, .5f, .1f, .9f), .7f, .5f, .18f);
            heroStamp.gameObject.SetActive(false);
        }

        void HeroBegin()
        {
            HeroStop();
            if (heroCard) heroSequence = StartCoroutine(HeroSequence());
        }

        void HeroStop()
        {
            if (heroSequence != null) { StopCoroutine(heroSequence); heroSequence = null; }
            heroPop = -1;
            if (heroGroup) heroGroup.alpha = 0;
        }

        IEnumerator Realtime(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }

        IEnumerator HeroSequence()
        {
            heroPop = -1; heroStampAt = -1; heroGroup.alpha = 0;
            heroTitle.text = DivaText.T(timeUp ? "timeup" : "hero.title");
            heroSub.text = DivaUi.Clock(finishedTime) + "   ·   " + DivaText.F("finish.tasks", game ? game.TasksDone : 0, game ? game.tasks.Length : 4) + "   ·   " + SkinName;
            heroSub.alpha = 0;
            heroScore.text = "0"; heroStamp.gameObject.SetActive(false);
            int finalScore = TotalScore;
            string pts = DivaText.Chinese ? "分" : "PTS";
            // 0.0 轰 + 象鸣（烟花和彩带在 EnterFinish 里照常放）
            sound.Play("boom", 1f); sound.Play("trumpet", .8f);
            yield return Realtime(.6f);
            // 0.6 卡片砸进来
            heroPop = Time.unscaledTime; sound.Play("hit", .9f, .7f);
            yield return Realtime(.4f);
            heroSub.alpha = 1;
            yield return Realtime(.3f);
            // 1.3 分数嘀嘀嘀涨上去（1.4 秒）
            float t0 = Time.unscaledTime, tick = 0;
            while (Time.unscaledTime - t0 < 1.4f)
            {
                float k = DivaUi.EaseOut((Time.unscaledTime - t0) / 1.4f);
                heroScore.text = Mathf.RoundToInt(finalScore * k).ToString("N0") + "<size=40%> " + pts + "</size>";
                if (Time.unscaledTime > tick) { tick = Time.unscaledTime + .06f; sound.Play("tick", .35f, 1 + k * .8f); }
                yield return null;
            }
            heroScore.text = finalScore.ToString("N0") + "<size=40%> " + pts + "</size>";
            sound.Play("confirm", .8f, 1.2f);
            yield return Realtime(.3f);
            sound.Play("cheer", .9f);
            yield return Realtime(.5f);
            // 3.2 图章：圈速奖牌（超时没有奖牌就"冠军"）
            heroStamp.text = DivaText.T(Medal != "" ? Medal : "hero.champion");
            heroStamp.color = Medal == "medal.silver" ? new Color(.85f, .9f, 1f) : Medal == "medal.bronze" ? new Color(1f, .65f, .4f) : GoldColour;
            heroStamp.gameObject.SetActive(true); heroStampAt = Time.unscaledTime;
            sound.Play("powerup", .6f, 1.4f);
            Debug.Log("DIVA_SHOW hero stamp " + heroStamp.text);
            heroSequence = null;
        }

        void UpdateHero()
        {
            if (!heroRoot || !heroRoot.gameObject.activeSelf) return;
            float t = Time.unscaledTime;
            if (heroPop < 0) { heroGroup.alpha = 0; return; }
            float ct = t - heroPop;
            heroGroup.alpha = Mathf.Clamp01(ct * 6);
            float pop = DivaUi.EaseOutBack(ct / .45f);
            heroCard.localScale = Vector3.one * Mathf.Lerp(1.8f, 1, pop);
            heroCard.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-8, -2, pop));
            if (heroStamp.gameObject.activeSelf)
            {
                float sp = DivaUi.EaseOutBack((t - heroStampAt) / .35f);
                heroStamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.6f, 1, sp);
                heroStamp.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18, -8, sp));
            }
        }
    }
}
