using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva.Show
{
    public enum Lang { En, Zh }

    /// <summary>
    /// 界面文字的中英文表。默认英文；右上角按钮或 L 键切换。
    /// 关掉了 Domain Reload（Enter Play Mode Options），所以每次进 Play 都要把静态状态复位成英文。
    /// </summary>
    public static class DivaText
    {
        public static Lang Current { get; private set; } = Lang.En;
        public static bool Chinese => Current == Lang.Zh;
        public static event Action Changed;
        /// <summary>中文开关。Carl 2026-10-13：中文显示有 bug，先把切换整个拿掉（文字表留着，之后修好再开）。</summary>
        public static bool LanguageSwitch = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = Lang.En; Changed = null; }

        public static void Set(Lang lang)
        {
            if (lang == Lang.Zh && !LanguageSwitch) return;
            if (lang == Current) return;
            Current = lang;
            Changed?.Invoke();
        }

        public static void Toggle() => Set(Chinese ? Lang.En : Lang.Zh);

        public static string T(string key) => Table.TryGetValue(key, out var v) ? (Chinese ? v.zh : v.en) : key;
        public static string F(string key, params object[] args) => string.Format(T(key), args);
        public static bool Has(string key) => Table.ContainsKey(key);

        // Eva 的任务名是英文写在场景里的；按关键词给出中文
        public static string Task(string label)
        {
            if (!Chinese || string.IsNullOrEmpty(label)) return label;
            string l = label.ToLowerInvariant();
            if (l.Contains("eat") || l.Contains("feed")) return "吃东西";
            if (l.Contains("drink") || l.Contains("water")) return "喝水";
            if (l.Contains("log") || l.Contains("step")) return "绕过树干";
            if (l.Contains("finish") || l.Contains("arch")) return "到达终点";
            return label;
        }

        // 自动队名：形容词 + 名词，按序号存，所以排行榜换语言时队名也跟着换
        public static readonly (string en, string zh)[] TeamAdjectives =
        {
            ("Pink", "粉红"), ("Turbo", "涡轮"), ("Bubbly", "泡泡"), ("Sparkle", "闪亮"), ("Rocket", "火箭"),
            ("Candy", "糖果"), ("Neon", "霓虹"), ("Mighty", "无敌"), ("Cosmic", "宇宙"), ("Fizzy", "汽水"),
        };
        public static readonly (string en, string zh)[] TeamNouns =
        {
            ("Trunks", "象鼻队"), ("Tuskers", "长牙队"), ("Splashers", "水花队"), ("Stompers", "踏地队"),
            ("Jumbos", "大象队"), ("Boosters", "推进队"), ("Bunnies", "兔兔队"), ("Peanuts", "花生队"),
        };
        public static string Team(int adjective, int noun)
        {
            var a = TeamAdjectives[Mathf.Abs(adjective) % TeamAdjectives.Length];
            var n = TeamNouns[Mathf.Abs(noun) % TeamNouns.Length];
            return Chinese ? a.zh + n.zh : a.en + " " + n.en;
        }

        static readonly Dictionary<string, (string en, string zh)> Table = new Dictionary<string, (string, string)>
        {
            ["game"] = ("DIVA SAFARI", "DIVA SAFARI"),
            ["lang"] = ("<b>EN</b>  <alpha=#66>中文", "<alpha=#66>EN  <alpha=#FF><b>中文</b>"),
            ["setup"] = ("SETUP", "设置"),

            ["select.title"] = ("CHOOSE YOUR MECH", "选择机甲涂装"),
            ["select.start"] = ("START", "开始"),
            ["select.players1"] = ("1 PLAYER", "单人"),
            ["select.players3"] = ("3 PLAYERS", "三人"),
            ["select.gesture"] = ("Raise one arm to switch  ·  Hold both hands above your head to start",
                                  "举起一只手切换  ·  双手举过头顶停一下开始"),
            ["select.keys"] = ("← → switch   ·   Enter start   ·   drag to turn", "← → 切换   ·   回车开始   ·   拖动旋转"),
            ["select.skin"] = ("SKIN {0}/{1}", "涂装 {0}/{1}"),

            ["status.test"] = ("Test controls: keyboard and sliders", "测试模式：键盘和滑块控制"),
            ["status.waiting"] = ("Step into the camera view", "请站到摄像头前"),
            ["status.start"] = ("Pick a colour and players, then START", "选好颜色和人数，点 START"),
            ["check.title"] = ("CAMERA CHECK", "摄像头识别"),
            ["check.sub1"] = ("One player does all three jobs. Step into the camera view and stand naturally, arms down",
                              "一个人负责全部三项。站进摄像头画面，自然站好，双手放下"),
            ["check.sub3"] = ("Three players side by side: P1 left · P2 middle · P3 right. Stand naturally, arms down",
                              "三位玩家并排站好：P1 左 · P2 中 · P3 右，自然站好，双手放下"),
            // 角色卡（和 DivaControlState 一致：P1 前进，P2 转弯，P3 瞄准 / 喷水 / 竖拇指吃喝和补水；大招任何人都能放）
            ["check.role1"] = ("WALKER", "走路"),
            ["check.role2"] = ("DIRECTION", "方向"),
            ["check.role3"] = ("ATTACK + EAT", "攻击 + 吃喝"),
            ["check.how1"] = ("Swing both arms up and down to walk", "双臂上下摆动就往前走，停下就停"),
            ["check.how2"] = ("Tilt your shoulders or head the way you want to turn", "往哪边压肩或歪头，就往哪边转"),
            ["check.how3"] = ("Raise one arm to aim · push both hands forward to spray\nThumbs-up = eat, drink & refill water",
                              "举一只手瞄准 · 双手前推喷水\n竖大拇指 = 吃喝 + 补水"),
            // 火柴人动画下面的小字（DivaStickFigure.CaptionKey）
            ["check.cap.swing"] = ("SWING ARMS", "摆臂"),
            ["check.cap.left"] = ("LEFT", "向左"),
            ["check.cap.right"] = ("RIGHT", "向右"),
            ["check.cap.shoot"] = ("SHOOT", "喷水"),
            ["check.cap.drink"] = ("DRINK + EAT", "喝水 + 吃东西"),
            ["check.ult1"] = ("Both hands above your head = MEGA BUBBLE (when charged)", "双手举过头顶 = 超级泡泡（充满后）"),
            ["check.ult3"] = ("Anyone: both hands above your head = MEGA BUBBLE (when charged)", "任何人：双手举过头顶 = 超级泡泡（充满后）"),
            ["check.ok"] = ("READY", "已识别"),
            ["check.wait"] = ("WAIT", "等待中"),
            ["check.hold"] = ("Recognised! Hold still… {0}", "识别成功！保持不动…{0}"),
            ["check.go"] = ("GO!", "开始！"),
            ["check.keys"] = ("Enter: skip (keyboard play)   ·   Esc: back", "回车：跳过（用键盘玩）   ·   Esc：返回"),
            ["status.camera"] = ("No camera picture: {0}  ·  F1 → Retry, or Test controls / keyboard", "摄像头没有画面：{0}  ·  按 F1 点 Retry，或改用 Test controls / 键盘"),
            ["status.waiting3"] = ("Waiting for {0}", "等待 {0} 入镜"),
            ["status.calibrating"] = ("Stand naturally… ready in {0}", "自然站好…{0} 秒后就绪"),
            ["status.ready"] = ("Ready! Gestures are on", "就绪！可以用手势了"),

            ["powerup"] = ("{0}  ·  ONLINE", "{0}  ·  启动"),
            ["go"] = ("GO!", "出发！"),

            ["hud.score"] = ("SCORE", "分数"),
            ["hud.time"] = ("TIME", "用时"),
            ["hud.left"] = ("LEFT", "剩余"),
            ["hud.tasks"] = ("TASKS", "任务"),
            ["hud.water"] = ("WATER", "水箱"),
            ["hud.hit"] = ("HIT", "命中"),

            ["skill.boost"] = ("BOOSTER RUSH", "推进冲刺"),
            ["skill.cannon"] = ("TRUNK CANNON", "象鼻水炮"),
            ["skill.bubble"] = ("BUBBLE REFILL", "泡泡补给"),
            ["skill.ult"] = ("MEGA BUBBLE", "超级泡泡"),
            ["skill.combo3"] = ("TRIPLE SPLASH", "三连击"),
            ["skill.combo5"] = ("SPLASH STORM", "水花风暴"),
            ["skill.task"] = ("TASK COMPLETE", "任务完成"),
            ["key.boost"] = ("RUN", "跑起来"),
            ["key.cannon"] = ("PUSH HANDS", "双手前推"),
            ["key.bubble"] = ("THUMBS UP (0.4s)", "竖起拇指保持0.4秒"),
            ["key.ult"] = ("BOTH HANDS UP · E", "双手举高 · E"),
            ["ult.ready"] = ("MEGA BUBBLE READY — hold both hands above your head!", "超级泡泡就绪——双手举过头顶！"),
            ["feed.hit"] = ("Target hit  +{0}", "命中靶子  +{0}"),
            ["feed.combo"] = ("{0}  +{1}", "{0}  +{1}"),
            ["feed.ult"] = ("{0} hit {1} targets", "{0} 打中 {1} 个靶子"),
            // ---- 比赛节奏（DivaShowMeta：PC 和 VR 共用）
            ["feed.candy"] = ("CANDY  +{0}", "糖果  +{0}"),
            ["feed.rocket"] = ("ROCKET!", "火箭！"),
            ["dash"] = ("ROCKET DASH", "火箭冲刺"),
            ["dash.ult"] = ("CHARGE!", "冲锋！"),
            ["dash.b"] = ("full speed ahead for 2.5 s", "2.5 秒全速前冲"),
            ["fire"] = ("ON FIRE!", "燃起来了！"),
            ["fire.b"] = ("score ×1.5 for 12 s", "12 秒内得分 ×1.5"),
            ["fire.name"] = ("ON FIRE", "燃烧"),
            ["fire.hint"] = ("HIT · CANDY · TASK", "打靶 · 吃糖 · 任务"),
            ["fire.on"] = ("×1.5  ·  {0}s", "×1.5  ·  {0} 秒"),
            ["golden"] = ("GOLDEN TARGET!", "黄金靶子！"),
            ["golden.b"] = ("3× points for 10 s", "10 秒内三倍分"),
            ["medal.gold"] = ("GOLD LAP", "金牌圈速"),
            ["medal.silver"] = ("SILVER LAP", "银牌圈速"),
            ["medal.bronze"] = ("BRONZE LAP", "铜牌圈速"),
            ["hero.title"] = ("PLAY OF THE RACE", "全场最佳"),
            ["hero.champion"] = ("CHAMPION", "冠军"),
            // 教练提示（单人 .b；三人模式 .b3 说明是哪位玩家的动作）
            ["coach.run"] = ("RUN!", "跑起来！"),
            ["coach.run.b"] = ("pump your arms (or W)  ·  follow the beacon", "摆动双臂（或 W）  ·  跟着光柱走"),
            ["coach.run.b3"] = ("P1: pump your arms  ·  P2: lean to steer", "P1 摆动双臂  ·  P2 身体倾斜转向"),
            ["coach.shoot"] = ("TARGET AHEAD", "前方有靶子"),
            ["coach.shoot.b"] = ("push both hands forward (or Space)", "双手往前推（或空格）"),
            ["coach.shoot.b3"] = ("P3: push both hands forward", "P3 双手往前推"),
            ["coach.eat"] = ("EAT HERE", "在这里吃"),
            ["coach.eat.b"] = ("thumbs-up 0.4 s (or F)", "竖拇指 0.4 秒（或 F）"),
            ["coach.eat.b3"] = ("P3: thumbs-up 0.4 s", "P3 竖拇指 0.4 秒"),
            ["coach.drink"] = ("DRINK HERE", "在这里喝水"),
            ["coach.drink.b"] = ("thumbs-up 0.4 s (or F)  ·  refills water too", "竖拇指 0.4 秒（或 F）  ·  顺便补水"),
            ["coach.drink.b3"] = ("P3: thumbs-up 0.4 s", "P3 竖拇指 0.4 秒"),
            ["coach.log"] = ("GO AROUND THE LOG", "绕过树干"),
            ["coach.log.b"] = ("just walk past it along the path", "顺着赛道走过去就行"),
            ["coach.water"] = ("WATER LOW", "水快没了"),
            ["coach.water.b"] = ("thumbs-up 0.4 s to refill (or F)", "竖拇指 0.4 秒补水（或 F）"),
            ["coach.water.b3"] = ("P3: thumbs-up 0.4 s to refill", "P3 竖拇指 0.4 秒补水"),
            ["coach.here"] = ("YOU'RE HERE", "到了"),
            // 终点前的冰墙（DivaShowWall）
            ["coach.wall"] = ("ICE WALL AHEAD", "前面有冰墙"),
            ["coach.wall.b"] = ("only MEGA BUBBLE breaks it  ·  push the wall to charge", "只有超级泡泡能炸开  ·  顶着墙推能充能"),
            ["coach.wall.b3"] = ("only MEGA BUBBLE breaks it  ·  P1: push the wall to charge", "只有超级泡泡能炸开  ·  P1 顶着墙推充能"),
            ["coach.wall.ult"] = ("SMASH IT — MEGA BUBBLE!", "放超级泡泡 炸开它！"),
            ["coach.wall.ult.b"] = ("both hands above your head (or E)", "双手举过头顶（或 E）"),
            ["wall.hint"] = ("MEGA BUBBLE TO BREAK", "超级泡泡才能炸开"),
            ["wall.hint.ult"] = ("MEGA BUBBLE NOW!", "放超级泡泡！"),
            ["wall.break"] = ("WALL SMASHED!", "冰墙炸开了！"),
            ["wall.break.b"] = ("+{0}  ·  charge through!", "+{0}  ·  冲过去！"),
            ["stat.candies"] = ("Candy", "糖果"),

            ["finish"] = ("COURSE COMPLETE", "完成赛道"),
            ["timeup"] = ("TIME UP", "时间到"),
            ["finish.sub"] = ("{0}   ·   {1} PTS", "{0}   ·   {1} 分"),
            ["finish.tasks"] = ("TASKS {0}/{1}", "完成任务 {0}/{1}"),

            ["highlight"] = ("HIGHLIGHT", "本局高光"),
            ["highlight.skip"] = ("Enter / both hands up to skip", "回车或双手举高跳过"),
            ["replay"] = ("REPLAY", "回放"),

            ["results"] = ("RESULTS", "本局结算"),
            ["board"] = ("LEADERBOARD", "排行榜"),
            ["again"] = ("PLAY AGAIN", "再来一局"),
            ["again.hint"] = ("Enter or hold both hands up", "回车或双手举过头顶"),
            ["col.rank"] = ("#", "名次"),
            ["col.team"] = ("TEAM", "队名"),
            ["col.score"] = ("SCORE", "分数"),
            ["col.time"] = ("TIME", "用时"),
            ["col.hits"] = ("HITS", "命中"),
            ["best"] = ("NEW BEST!", "新纪录！"),
            ["rank"] = ("RANK #{0}", "第 {0} 名"),

            ["role.solo"] = ("PILOT", "驾驶员"),
            ["role.p1"] = ("P1  MOVE", "P1  移动"),
            ["role.p2"] = ("P2  TURN", "P2  转向"),
            ["role.p3"] = ("P3  WATER", "P3  水枪"),
            ["title.sharp"] = ("SHARPSHOOTER", "神射手"),
            ["title.speed"] = ("SPEED DEMON", "极速冲刺"),
            ["title.driver"] = ("SMOOTH DRIVER", "方向大师"),
            ["title.bubble"] = ("BUBBLE MASTER", "泡泡大师"),
            ["title.explorer"] = ("EXPLORER", "探险家"),
            ["stat.distance"] = ("Distance", "移动距离"),
            ["stat.top"] = ("Top speed", "最高速度"),
            ["stat.turn"] = ("Turned", "转向角度"),
            ["stat.hits"] = ("Hits / shots", "命中 / 发射"),
            ["stat.accuracy"] = ("Accuracy", "命中率"),
            ["stat.bubbles"] = ("Bubble time", "泡泡时间"),
            ["stat.tasks"] = ("Tasks", "任务"),
            ["stat.combo"] = ("Best combo", "最高连击"),
            ["unit.m"] = ("{0:0} m", "{0:0} 米"),
            ["unit.ms"] = ("{0:0.0} m/s", "{0:0.0} 米/秒"),
            ["unit.deg"] = ("{0:0}°", "{0:0}°"),
            ["unit.s"] = ("{0:0.0} s", "{0:0.0} 秒"),
        };
    }
}
