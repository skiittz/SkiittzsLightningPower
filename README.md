# Skiittz's Lightning Power

A Space Engineers mod that lets you harness lightning strikes as a source of electricity for your grid.

---

## Overview

On planets with weather, lightning can be a deadly hazard — but with this mod, it becomes a **free energy source**. Build lightning rods to attract strikes and capacitors to store the captured energy, then feed that power into your grid.

---

## Blocks

### ⚡ Lightning Rod (Decoy Block)

Every **Decoy** block on your grid doubles as a **Lightning Rod**. When lightning strikes a decoy, instead of taking damage, the block absorbs the strike and converts it into usable electricity.

**How it works:**

- When a lightning bolt hits a decoy, the strike's energy is captured instead of dealing damage.
- If there are **Lightning Capacitors** on the same grid, the captured energy is split evenly among all connected capacitors for storage.
- If there are **no capacitors** on the grid, the decoy will try to push the energy directly into the grid as a temporary power source. This power fades over time.
- **Be careful:** If a decoy is producing more power than the grid can use and there are no capacitors to absorb it, the excess energy will overload and damage the decoy itself.
- The decoy must be **powered on and functional** to capture lightning.

You can check a lightning rod's status in the block's info panel, which shows its current power output.

---

### 🔋 Lightning Capacitor

A dedicated energy storage block that can **only be charged by lightning** — it cannot draw power from the grid like a normal battery. Available in both **large grid** and **small grid** variants.

**How it works:**

- Capacitors receive energy from lightning rods (decoys) on the same grid.
- Each capacitor can store up to **1 MWh** of energy.
- Stored energy is discharged back into the grid at up to **10 MW**.
- Energy drains naturally as your grid consumes it, just like a battery.

**Overload Warning:**

- If a lightning strike delivers more energy than a capacitor can hold, the **excess energy overloads the capacitor**, dealing damage to it.
- If the overload **destroys** the capacitor, it will **explode**! The explosion's size and damage scale with how much excess energy caused the overload — bigger overloads mean bigger booms.
- When a capacitor is at full charge, its info panel will display a warning. Consider building multiple capacitors to spread the load and avoid overloads.

You can check a capacitor's status in the block's info panel, which shows:

- Current stored energy (out of 1 MWh max)
- Current power output to the grid
- Maximum discharge rate
- A warning when at full capacity

---

## Tips & Strategy

1. **Build multiple capacitors.** Lightning energy is split evenly across all capacitors on the grid, reducing the chance of any single one overloading.
2. **More decoys = more coverage.** Having several decoys spread across your base increases the chance of capturing strikes.
3. **Watch your charge levels.** If all your capacitors are nearly full when a big strike hits, the excess can chain into overloads and explosions.
4. **Use the power wisely.** Capacitors discharge at up to 10 MW — they can cover peak demand or act as backup power, but they only recharge from lightning, not from the grid.
5. **Planet-side only.** Lightning is a weather event, so this mod is most useful on planets with storms.
