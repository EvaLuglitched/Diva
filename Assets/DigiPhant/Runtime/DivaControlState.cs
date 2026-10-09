using System;

namespace DigiPhant
{
    [Serializable] public class DivaGestureFrame
    {
        public float go, steer, aim, confidence;
        public bool shoot, drink, ult;
    }

    // This state has no Unity dependency so camera-loss and gesture transitions are testable.
    public sealed class DivaControlState
    {
        readonly DivaGestureFrame[] people = new DivaGestureFrame[4];
        readonly float[] seen = { -1000, -1000, -1000, -1000 };
        public float Timeout = .5f, MinimumConfidence = .3f, BurstInterval = .12f;
        public float HoldDelay = .4f;
        public float WaterPerBurst = .008f, RefillPerSecond = .3f;
        public float Forward { get; private set; }
        public float Turn { get; private set; }
        public float Aim { get; private set; }
        public bool Shoot { get; private set; }
        public bool Drink { get; private set; }
        /// <summary>A player holds both hands above the head (ultimate; also "confirm" in menus). Read even when Locked.</summary>
        public bool Ult { get; private set; }
        /// <summary>Raised-arm direction (-1 left, 1 right) for menus. Read even when Locked.</summary>
        public float MenuAim { get; private set; }
        /// <summary>Menus and cutscenes: gestures are still read (Ult, MenuAim) but do not drive the elephant.</summary>
        public bool Locked;
        public float Water { get; private set; } = 1;
        public int Bursts { get; private set; }
        bool wasShooting;
        float nextBurst;
        public void ClearPeople() { Array.Clear(people, 0, people.Length); }
        public void Clear()
        {
            Array.Clear(people, 0, people.Length);
            Forward = Turn = Aim = MenuAim = 0; Shoot = Drink = Ult = wasShooting = false; Bursts = 0;
        }
        public void Accept(int slot, float go, float steer, float aim, bool shoot, bool drink, float confidence, float now, bool ult = false)
        {
            if (slot < 1 || slot > 4) return;
            people[slot - 1] = new DivaGestureFrame { go = go, steer = steer, aim = aim, shoot = shoot, drink = drink, ult = ult, confidence = confidence };
            seen[slot - 1] = now;
        }
        public bool Visible(int slot, float now) => slot >= 1 && slot <= 4 && people[slot - 1] != null &&
            now - seen[slot - 1] <= Timeout && people[slot - 1].confidence >= MinimumConfidence;
        DivaGestureFrame Read(int slot, float now) => Visible(slot, now) ? people[slot - 1] : null;
        /// <summary>soloRole (one person): 0 = that person has every control, 1..3 = only that player's role.</summary>
        public void Step(float now, float dt, int count, int soloRole, bool ready)
        {
            Forward = Turn = Aim = MenuAim = 0; Shoot = Drink = Ult = false; Bursts = 0;
            if (!ready) { wasShooting = false; return; }
            bool all = count == 1 && soloRole == 0;
            var p1 = count == 1 ? (soloRole == 1 || all ? Read(1, now) : null) : Read(1, now);
            var p2 = count == 1 ? (soloRole == 2 || all ? Read(1, now) : null) : Read(2, now);
            var p3 = count == 1 ? (soloRole == 3 || all ? Read(1, now) : null) : Read(3, now);
            // Ultimate and menu pointing come from whoever does them (one player, or anyone in the team).
            for (int slot = 1; slot <= (count == 1 ? 1 : 3); slot++)
            {
                var p = Read(slot, now);
                if (p == null) continue;
                Ult |= p.ult;
                if (MenuAim == 0) MenuAim = Math.Clamp(p.aim, -1, 1);
            }
            if (Locked) { wasShooting = false; return; }
            if (p1 != null) Forward = Math.Clamp(p1.go, 0, 1);
            if (p2 != null) Turn = Math.Clamp(p2.steer, -1, 1);
            if (p3 != null)
            {
                Aim = Math.Clamp(p3.aim, -1, 1);
                Drink = p3.drink;
                if (Drink) Water = Math.Min(1, Water + Math.Max(0, RefillPerSecond) * Math.Clamp(dt, 0, .1f));
                Shoot = p3.shoot && !Drink && Water >= Math.Max(.0001f, WaterPerBurst);
            }
            // One person doing everything: moving the arms into the spray / drink pose must not also walk,
            // and drinking (hands at the mouth) must not steer.
            if (all && (Shoot || Drink || Ult)) Forward = 0;
            if (all && (Drink || Ult)) Turn = 0;
            if (Shoot && (!wasShooting || now >= nextBurst))
            {
                Bursts = 1; nextBurst = now + (!wasShooting ? Math.Max(.04f, HoldDelay) : Math.Max(.04f, BurstInterval));
                Water = Math.Max(0, Water - Math.Max(.0001f, WaterPerBurst));
            }
            wasShooting = Shoot;
        }
        public void FillTank() { Water = 1; }
    }
}
