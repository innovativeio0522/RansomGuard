# Design System Specification: The Sentinel Aesthetic

## 1. Overview & Creative North Star
**Creative North Star: "The Silent Guardian"**

In the world of cybersecurity, trust is not built through loud decorations; it is built through precision, calm, and unwavering clarity. This design system departs from the "noisy" dashboard tropes of the past decade. Instead of neon grids and vibrating lines, we employ a **high-end editorial approach** to data. 

The system prioritizes **Tonal Authority**. We move away from the "template" look by using intentional asymmetry, overlapping surfaces, and a hierarchy driven by depth rather than borders. This creates a "WPF-Plus" experience—a desktop application that feels native to Windows but carries the bespoke polish of a luxury timepiece.

## 2. Colors: The Depth of Security
Our palette is rooted in the "Midnight Tier." We do not use pure black; we use varying densities of navy and charcoal to create a sense of infinite digital space.

### The Palette (Material Design Mapping)
*   **Background / Surface Dim:** `#0f131d` (The foundation)
*   **Primary (Action):** `#adc6ff` (Tech-blue for intent)
*   **Secondary (Safe):** `#4edea3` (Emerald for protection)
*   **Tertiary (Warning):** `#ffb3ad` (Amber/Ruby tones for urgency)
*   **Surface Containers:** Range from `Lowest (#0a0e18)` to `Highest (#313540)`

### The "No-Line" Rule
**Explicit Instruction:** Do not use 1px solid borders to define sections. Traditional borders create visual clutter that traps the eye. Instead:
*   Define boundaries through **Background Shifts**. A `surface-container-low` card sitting on a `surface` background creates a natural edge.
*   Use **Negative Space**. Let the alignment of typography define the vertical and horizontal flow.

### The "Glass & Gradient" Rule
To elevate the UI beyond a standard utility, use **Glassmorphism** for floating elements (e.g., Modals, Context Menus).
*   **Implementation:** Use a semi-transparent `surface-container-high` with a 20px-40px `backdrop-filter: blur()`.
*   **Signature Textures:** Apply a subtle linear gradient to primary action buttons (from `primary` to `primary_container`). This provides a "jewel" effect that differentiates interactive elements from static data.

## 3. Typography: The Editorial Edge
We use **Inter** for its mathematical precision and high x-height, ensuring legibility even at small scales for data-dense tables.

*   **Display (lg/md):** Use sparingly for "State of Security" summaries. High-contrast (Thin weight vs. Extra Bold) creates a signature look.
*   **Title (md/sm):** These are your anchors. Use `title-md` for card headings to provide an authoritative structure.
*   **Body (md/sm):** The workhorse. Always prioritize line height (1.5x) to ensure small business users don't suffer from fatigue when reading threat logs.
*   **Label (md/sm):** Use `label-sm` in all-caps with a `0.05em` letter-spacing for metadata and status badges. This evokes a technical, "instrument-panel" feel.

## 4. Elevation & Depth: Tonal Layering
In this design system, "Up" is "Lighter." We simulate physical height by increasing the luminance of the container.

*   **The Layering Principle:** 
    *   **Base:** `surface`
    *   **Sectioning:** `surface-container-low`
    *   **Interactive Cards:** `surface-container-high`
*   **Ambient Shadows:** For floating elements, use a shadow with a 32px blur, 0% spread, and 6% opacity. The shadow color should be `#000000` but softened by the underlying navy background.
*   **The "Ghost Border" Fallback:** If accessibility requires a stroke (e.g., in high-contrast mode), use `outline-variant` at **15% opacity**. It should be felt, not seen.

## 5. Components: Precision Primitives

### Cards & Data Containers
*   **Constraint:** Forbid divider lines within cards.
*   **Execution:** Use 16px to 24px of vertical padding to separate content blocks. Use `surface-container-highest` as a subtle "header" background for cards to anchor titles.

### Buttons
*   **Primary:** High-saturation blue (`primary`). Roundedness: `md` (0.375rem).
*   **Secondary/Ghost:** No background fill. Use `on-surface` typography with a `primary` glow on hover.
*   **Status Buttons:** For "Fix Now" (Critical), use `error_container` with `on_error_container` text. This is a deliberate "Stop" signal.

### Status Indicators (The "Pulse")
*   **Safe:** A `secondary` (Emerald) chip with a 10% opacity background and a solid 4px "status dot."
*   **Threat:** A `tertiary` (Ruby) container. For critical threats, add a subtle 2px inner-glow to the container to make it appear "urgent."

### Input Fields
*   **State:** Default state is `surface-container-highest` with no border. 
*   **Active:** Transition to a 1px `primary` "Ghost Border" and increase the brightness of the background slightly.

### Cybersecurity-Specific: The "Threat Timeline"
*   Avoid standard list items. Use a vertical "Thread" layout where events are connected by a 2px `outline-variant` path, emphasizing the chronological flow of a security breach.

## 6. Do's and Don'ts

### Do
*   **Do** embrace "Breathing Room." Even in a data-dense desktop app, white space (or navy space) is what prevents a user from feeling overwhelmed.
*   **Do** use asymmetrical layouts for the dashboard (e.g., a 70/30 split) to guide the eye from "Global Status" to "Actionable Details."
*   **Do** use Phosphor Icons in "Thin" or "Light" weights to maintain the high-end feel.

### Don't
*   **Don't** use 100% white text. Use `on-surface-variant` (`#c2c6d6`) for body text to reduce eye strain.
*   **Don't** use standard Windows "system" scrollbars. Implement a custom, thin `surface-variant` thumb with no track background.
*   **Don't** use sharp 90-degree corners. The `DEFAULT` (0.25rem) or `md` (0.375rem) roundedness is required to soften the "industrial" feel of the app.