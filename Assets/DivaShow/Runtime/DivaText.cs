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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = Lang.En; Changed = null; }

        public static void Set(Lang lang)
        {
            if (lang == Current) return;
            Current = lang;
            Changed?.Invoke();
        }

        public static void Toggle() => Set(Chinese ? Lang.En : Lang.Zh);

        public static string T(string key)
        {
            // VR 里有 ".vr" 版本的就用它（操作说明换成 Vision Pro 的手势）
            if (Diva.VR.DivaVR.Active && Table.TryGetValue(key + ".vr", out var vr)) return Chinese ? vr.zh : vr.en;
            return Table.TryGetValue(key, out var v) ? (Chinese ? v.zh : v.en) : key;
        }
        public static string F(string key, params object[] args) => string.Format(T(key), args);
        public static bool Has(string key) => Table.ContainsKey(key);

        // Eva 的任务名是英文写在场景里的；按关键词给出中文
        public static string Task(string label)
        {
            if (!Chinese || string.IsNullOrEmpty(label)) return label;
            string l = label.ToLowerInvariant();
            if (l.Contains("eat") || l.Contains("feed")) return "吃东西";
            if (l.Contains("drink") || l.Contains("water")) return "喝水";
            if (l.Contains("log") || l.Contains("step")) return "跨过木头";
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
            // ---- Vision Pro（VR 模式下自动替换上面同名的说明）
            ["select.gesture.vr"] = ("Pinch to switch paint  ·  Raise both hands to start",
                                     "捏一下换涂装  ·  双手举过头顶开始"),
            ["select.keys.vr"] = ("Sit or stand comfortably  ·  your cockpit follows you", "坐着站着都可以  ·  驾驶舱会跟着你"),
            ["status.test.vr"] = ("Pilot ready — pinch, lean, raise your hands", "驾驶员就绪——捏、倾身、举手都可以"),
            ["key.boost.vr"] = ("LEAN FORWARD", "身体前倾"),
            ["key.cannon.vr"] = ("LOOK + PINCH", "看着靶子捏"),
            ["key.bubble.vr"] = ("HAND TO MOUTH", "手放嘴边"),
            ["key.ult.vr"] = ("BOTH HANDS UP", "双手举高"),
            ["vr.card.title"] = ("HOW TO PILOT", "驾驶方法"),
            ["vr.watch"] = ("HOW TO PLAY", "怎么玩"), ["vr.watch.sub"] = ("WATCH THE GLOWING HAND", "看发光的手怎么做"),
            ["vr.watch.1"] = ("LOOK AT A BUTTON · PINCH THUMB AND INDEX", "看着按钮 · 拇指食指捏一下"), ["vr.watch.1b"] = ("= that presses it", "= 这就是按下它"),
            ["vr.watch.2"] = ("GRAB THE STICK BY YOUR RIGHT KNEE · PUSH FORWARD", "握住右膝旁的操纵杆 · 往前推"), ["vr.watch.2b"] = ("= run · push more = faster · let go = stop", "= 前进 · 推得多跑得快 · 松手就停"),
            ["vr.watch.3"] = ("PUSH THE STICK LEFT OR RIGHT · OR TILT YOUR HEAD", "杆往左或往右推 · 或者歪头"), ["vr.watch.3b"] = ("= turn", "= 转弯"),
            ["vr.watch.4"] = ("LOOK AT A TARGET · PINCH", "看着靶子 · 捏一下"), ["vr.watch.4b"] = ("= trunk water cannon (uses water)", "= 象鼻水炮（耗水）"),
            ["vr.watch.5"] = ("BRING A HAND TO YOUR MOUTH · OR THUMBS-UP", "一只手放到嘴边 · 或者竖大拇指"), ["vr.watch.5b"] = ("= eat, drink, refill water", "= 吃东西、喝水、补水"),
            ["vr.watch.6"] = ("RAISE BOTH HANDS TO EYE LEVEL · HOLD", "双手举到眼前 · 举住"), ["vr.watch.6b"] = ("= MEGA BUBBLE when the charge is full", "= 能量满时放超级泡泡"),
            ["vr.watch.done"] = ("NOW YOU TRY!", "该你试试了！"),
            ["vr.tour.begin"] = ("PINCH TO BEGIN", "捏一下 出发"),
            ["vr.tour.begin.b"] = ("your bubble is waiting", "你的泡泡在等你"),
            ["vr.tour.title"] = ("CANDY TOWN", "糖果镇"),
            ["vr.tour.course"] = ("your course — four stops, then the finish", "你的赛道：四站，然后冲线"),
            ["vr.tour.land"] = ("LANDING ON YOUR MECH", "降落到你的机甲上"),
            ["vr.tour.land.b"] = ("hold still — it's all yours", "坐稳，它是你的了"),
            ["coach.run.vr"] = ("GRAB THE LEVER, PUSH FORWARD", "握住操纵杆 往前推"),
            ["coach.run.b.vr"] = ("follow the pink beam · push the lever left or right to turn", "跟着粉色光柱跑 · 杆往左右推转弯"),
            ["vr.guide.step"] = ("STEP {0} OF {1}", "第 {0} 步 / 共 {1} 步"),
            ["vr.guide.pick"] = ("PICK YOUR PAINT", "选涂装"),
            ["vr.guide.pick.a"] = ("PINCH  =  try the next paint", "捏一下  =  换下一个涂装"),
            ["vr.guide.pick.b"] = ("look at a button and pinch to press it", "看着按钮捏一下  =  按下这个按钮"),
            ["vr.guide.prev"] = ("<", "<"),
            ["vr.guide.next"] = (">", ">"),
            ["vr.guide.choose"] = ("CHOOSE THIS PAINT", "就选这个"),
            ["vr.guide.learn"] = ("LEARN TO DRIVE", "学开大象"),
            ["vr.guide.learn.lean"] = ("GRAB THE LEVER, PUSH FORWARD", "握住右手边的操纵杆 往前推"),
            ["vr.guide.learn.lean.b"] = ("pinch or grip the glowing knob · push more to run", "捏住或握住发亮的杆头 · 推得多就跑"),
            ["vr.guide.learn.turn"] = ("PUSH THE LEVER LEFT / RIGHT", "杆往左右推"),
            ["vr.guide.learn.turn.b"] = ("turn · or tilt your head · let go to stop", "转弯 · 或者歪头 · 松手就停"),
            ["vr.guide.learn.pinch"] = ("LOOK + PINCH", "看着目标 捏一下"),
            ["vr.guide.learn.pinch.b"] = ("trunk water cannon", "象鼻水炮"),
            ["vr.guide.learn.mouth"] = ("HAND TO MOUTH", "手放到嘴边"),
            ["vr.guide.learn.mouth.b"] = ("eat & drink", "吃东西、喝水"),
            ["vr.guide.skip"] = ("SKIP", "跳过"),
            ["vr.guide.ready"] = ("READY? LET'S GO!", "准备好了？出发！"),
            ["vr.guide.ready.a"] = ("RAISE BOTH HANDS to eye level", "双手举到眼前"),
            ["vr.guide.ready.b"] = ("hold until the bar fills · in the race this fires your MEGA BUBBLE", "举住直到进度条满 · 比赛里这个动作是大招「超级泡泡」"),
            ["vr.guide.start"] = ("START", "出发"),
            ["vr.guide.back"] = ("< PAINT", "< 换涂装"),
            ["vr.guide.results"] = ("NICE RUN!", "跑得漂亮！"),
            ["vr.guide.again"] = ("RAISE BOTH HANDS to play again", "双手举到眼前  =  再来一局"),
            ["vr.guide.again.b"] = ("or look at the button and pinch", "或者看着按钮捏一下"),
            ["vr.guide.again.btn"] = ("PLAY AGAIN", "再来一局"),
            ["vr.guide.locked"] = ("LOCKED IN!", "就它了！"),
            ["vr.guide.nice"] = ("NICE!", "漂亮！"),
            ["vr.feed.task"] = ("DONE!", "完成！"),
            ["vr.stat.speed"] = ("SPEED", "速度"), ["vr.stat.cannon"] = ("CANNON", "水炮"), ["vr.stat.bubble"] = ("BUBBLE", "泡泡"),
            ["vr.ult.ready"] = ("MEGA BUBBLE READY", "大招已就绪"), ["vr.ult.ready.b"] = ("raise both hands to eye level", "双手举到眼前"),
            ["coach.water.vr"] = ("WATER LOW — REFILL", "水快没了 去补水"), ["coach.water.b.vr"] = ("hand to mouth at the fountain", "到水池边手放嘴边"),
            ["vr.finish.sub"] = ("full lap of Candy Town", "糖果镇 完整一圈"),
            ["vr.menu.sub"] = ("one lap round Candy Town  ·  four stops  ·  the finish arch", "绕糖果镇一圈 · 四个站 · 冲过终点拱门"),
            ["vr.menu.start"] = ("START THE RACE", "开始比赛"),
            ["vr.menu.hint"] = ("face a button and pinch", "头朝向按钮，捏一下"),
            ["vr.menu.exit"] = ("Digital Crown = leave the game any time", "数码表冠 = 随时退出游戏"),
            ["vr.menu.mission"] = ("YOUR MISSION  ·  ONE LAP, FOUR STOPS", "你的任务 · 跑一圈，四个站"),
            ["vr.menu.mission.b"] = ("shoot targets for points  ·  MEGA BUBBLE when charged  ·  cross the finish arch", "打靶得分 · 能量满放超级泡泡 · 冲过终点拱门"),
            ["vr.pause.btn"] = ("PAUSE", "暂停"), ["vr.pause"] = ("PAUSED", "暂停中"),
            ["vr.pause.a"] = ("the race is frozen", "比赛冻住了"), ["vr.pause.b"] = ("look at a button and pinch", "看着按钮捏一下"),
            ["vr.pause.continue"] = ("CONTINUE", "继续"), ["vr.pause.replay"] = ("REPLAY TUTORIAL", "重看教程"),
            ["vr.pause.quit"] = ("QUIT TO PAINT SELECT", "回选涂装"), ["vr.pause.quit2"] = ("SURE? QUIT", "确定退出？"),
            ["vr.guide.replay"] = ("REPLAY TUTORIAL", "重看教程"),
            ["coach.eat.vr"] = ("HAND TO MOUTH — EAT", "手放嘴边 吃东西"),
            ["coach.eat.b.vr"] = ("your trunk grabs a snack", "象鼻会去够吃的"),
            ["coach.log.vr"] = ("KEEP PUSHING", "继续推"),
            ["coach.log.b.vr"] = ("walk over the fallen log", "从倒下的木头上走过去"),
            ["coach.shoot.vr"] = ("LOOK AT THE TARGET + PINCH", "看着靶子 捏一下"),
            ["coach.shoot.b.vr"] = ("trunk cannon", "象鼻水炮"),
            ["coach.drink.vr"] = ("HAND TO MOUTH — DRINK", "手放嘴边 喝水"),
            ["coach.drink.b.vr"] = ("refill at the fountain", "在这里补水"),
            ["vr.coach.ult"] = ("RAISE BOTH HANDS — MEGA BUBBLE", "举起双手 放大招"),
            ["vr.coach.ult.b"] = ("palms forward at eye level, hold", "举到眼前保持住"),
            ["vr.card.1"] = ("LEVER FORWARD", "操纵杆前推"), ["vr.card.1b"] = ("run", "前进"),
            ["vr.card.2"] = ("LEVER LEFT / RIGHT · TILT HEAD", "操纵杆左右 · 歪头"), ["vr.card.2b"] = ("steer", "转向"),
            ["vr.card.3"] = ("LOOK + PINCH", "看着靶子捏"), ["vr.card.3b"] = ("trunk cannon", "象鼻水炮"),
            ["vr.card.4"] = ("HAND TO MOUTH", "手放嘴边"), ["vr.card.4b"] = ("refill", "补水"),
            ["vr.card.5"] = ("BOTH HANDS UP", "双手举高"), ["vr.card.5b"] = ("MEGA BUBBLE", "超级泡泡"),
            ["select.skin"] = ("SKIN {0}/{1}", "涂装 {0}/{1}"),

            ["status.test"] = ("Test controls: keyboard and sliders", "测试模式：键盘和滑块控制"),
            ["status.waiting"] = ("Step into the camera view", "请站到摄像头前"),
            ["status.start"] = ("Pick a colour and players, then START", "选好颜色和人数，点 START"),
            ["check.title"] = ("CAMERA CHECK", "摄像头识别"),
            ["check.sub1"] = ("Step into the camera view and stand naturally, arms down", "站进摄像头画面，自然站好，双手放下"),
            ["check.sub3"] = ("Three players side by side: P1 left · P2 middle · P3 right. Stand naturally, arms down",
                              "三位玩家并排站好：P1 左 · P2 中 · P3 右，自然站好，双手放下"),
            ["check.you"] = ("YOU", "玩家"),
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
            ["key.ult"] = ("BOTH HANDS UP · U", "双手举高 · U"),
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
            ["coach.run.b"] = ("pump your arms to go (or hold W)  ·  follow the beacon to the next stop", "摆动双臂前进（或按住 W）  ·  跟着光柱去下一站"),
            ["coach.run.b3"] = ("P1: pump your arms to go  ·  P2: lean to steer toward the beacon", "P1 摆动双臂前进  ·  P2 身体倾斜朝光柱转"),
            ["coach.shoot"] = ("TARGET AHEAD", "前方有靶子"),
            ["coach.shoot.b"] = ("push both hands forward to spray (or Space)  ·  the lock marker shows what you'll hit", "双手往前推喷水（或空格）  ·  锁定标记就是会打中的靶子"),
            ["coach.shoot.b3"] = ("P3: push both hands forward to spray  ·  raise an arm to aim", "P3 双手往前推喷水  ·  举一只手瞄准"),
            ["coach.eat"] = ("EAT HERE", "在这里吃"),
            ["coach.eat.b"] = ("hold a thumbs-up for 0.4 s (or F)", "竖起拇指保持 0.4 秒（或按 F）"),
            ["coach.eat.b3"] = ("P3: hold a thumbs-up for 0.4 s", "P3 竖起拇指保持 0.4 秒"),
            ["coach.drink"] = ("DRINK HERE", "在这里喝水"),
            ["coach.drink.b"] = ("hold a thumbs-up for 0.4 s (or F)  ·  this also refills your water", "竖起拇指保持 0.4 秒（或按 F）  ·  顺便把水补满"),
            ["coach.drink.b3"] = ("P3: hold a thumbs-up for 0.4 s  ·  this also refills the water", "P3 竖起拇指保持 0.4 秒  ·  顺便把水补满"),
            ["coach.log"] = ("KEEP RUNNING", "继续跑"),
            ["coach.log.b"] = ("walk right over the fallen log", "直接从倒下的木头上走过去"),
            ["coach.water"] = ("WATER LOW", "水快没了"),
            ["coach.water.b"] = ("hold a thumbs-up for 0.4 s to refill (or F)", "竖起拇指保持 0.4 秒补水（或按 F）"),
            ["coach.water.b3"] = ("P3: hold a thumbs-up for 0.4 s to refill", "P3 竖起拇指保持 0.4 秒补水"),
            ["coach.here"] = ("YOU'RE HERE", "到了"),
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
