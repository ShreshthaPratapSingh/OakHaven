# OakHaven Official Website — Agent Guide

This document defines the architecture, conventions, and quality standards for the OakHaven official website. It exists so any agent working on the project can make consistent decisions without inventing game content or drifting from the approved design direction.

## Project context

- **Game:** OakHaven — a first-person→third-person networked survival/crafting game built in Unity 6.6 / URP.
- **Website purpose:** A cinematic, premium-feeling official catalogue that showcases the game's world, story, gameplay mechanics, characters, environments, artwork, and development progress.
- **Important constraint:** The website must feel like a real indie game's official home, not a generic SaaS landing page or developer portfolio. It must never invent lore, characters, locations, or mechanics. Use placeholders wherever game information is unavailable.
- **Scope boundary:** This is a separate sub-project under `OakHaven/Website`. Do not modify files outside this folder unless explicitly asked.

## 1. Technology stack and coding conventions

### Stack

- **Framework:** React 18+
- **Build tool:** Vite
- **Language:** TypeScript (strict mode preferred)
- **Styling:** Tailwind CSS
- **Routing:** React Router (plain object-based route config)
- **Icons:** Lucide React (or a single icon library — do not mix multiple icon sets)
- **Optional additions only when justified:**
  - A lightweight animation library such as Framer Motion for orchestrated entrance animations.
  - A headless UI primitive library (e.g., Radix UI) for menus, dialogs, and galleries if custom implementation becomes unwieldy.
  - Keep dependency count low. Prefer native Web APIs and browser features when practical.

### Coding conventions

- Use **functional components** with hooks. No class components.
- Prefer **named exports** for components so tooling can track usage.
- Use explicit, descriptive names: `CinematicHero`, `MediaGallery`, `DevelopmentFeed`, not `Hero`, `Gallery`, `Feed`.
- Co-locate styles using Tailwind utility classes. Avoid separate CSS files for one-off page styles.
- Keep components **focused and reusable**. If a component is only used in one place and cannot be generalized, it should still be simple enough to read in one screen.
- Write TypeScript interfaces for all content data. No `any` for domain models.
- Group related hooks, helpers, and constants close to where they are used.
- Use `import type` where applicable to avoid unnecessary runtime imports.
- Prefer early returns and guard clauses.
- Keep side effects isolated in `useEffect` and custom hooks.

### File naming

- Components: `PascalCase.tsx` (e.g., `SiteHeader.tsx`)
- Hooks: `camelCase.ts` with `use` prefix (e.g., `useMediaQuery.ts`)
- Utilities: `camelCase.ts` (e.g., `formatDate.ts`)
- Content manifests: `kebab-case.ts` (e.g., `development-posts.ts`)
- Constants: `SCREAMING_SNAKE_CASE` inside `UPPER_SNAKE_CASE.ts` files when exported at module level.

### Code quality rules

- Never invent game content inside code, comments, or placeholder data.
- Never embed unrelated or stock imagery paths as fallback for game screenshots.
- All user-facing strings that describe the game must be sourced from approved content files or clearly marked as `// TODO: awaiting approved copy`.

## 2. Component architecture

Use a layered architecture that separates layout, pages, content, and shared primitives.

```text
src/
├── app/
│   ├── App.tsx                 # route provider + site shell
│   └── routes.tsx              # central route definitions
├── components/
│   ├── layout/                 # global layout pieces
│   │   ├── SiteHeader.tsx
│   │   ├── SiteFooter.tsx
│   │   ├── MainNavigation.tsx
│   │   ├── MobileNavigation.tsx
│   │   └── PageLayout.tsx
│   ├── media/                  # image, video, gallery primitives
│   │   ├── ResponsiveImage.tsx
│   │   ├── VideoFigure.tsx
│   │   ├── MediaLightbox.tsx
│   │   └── MediaGrid.tsx
│   ├── content/                # reusable content blocks
│   │   ├── PageIntro.tsx
│   │   ├── CinematicHero.tsx
│   │   ├── ImageFeature.tsx
│   │   ├── ContentSection.tsx
│   │   ├── DevelopmentEntry.tsx
│   │   └── OfficialLinks.tsx
│   └── ui/                     # smallest presentational atoms
│       ├── Button.tsx
│       ├── IconButton.tsx
│       ├── Badge.tsx
│       └── Skeleton.tsx
├── pages/
│   ├── HomePage.tsx
│   ├── GamePage.tsx
│   ├── MediaPage.tsx
│   └── DevelopmentPage.tsx
├── content/
│   ├── site.ts                 # global metadata, nav, footer links
│   ├── game.ts                 # approved game description and mechanics
│   ├── media.ts                # gallery entries and metadata
│   └── development.ts          # devlog posts
├── hooks/
│   ├── useScrollPosition.ts
│   ├── useReducedMotion.ts
│   └── useLockBodyScroll.ts
├── lib/
│   └── utils.ts                # cn() helper and small utilities
├── styles/
│   └── globals.css             # Tailwind directives + base styles
└── types/
    └── index.ts                # shared TypeScript definitions
```

### Component responsibilities

- **Layout components** own the frame: header, footer, navigation, and page gutters. They should not contain game-specific content.
- **Page components** assemble sections and wire them to content manifests. Keep them thin.
- **Content components** render a specific type of content block and accept props, not raw markup.
- **Media components** handle responsive images, video posters, captions, and gallery behavior.
- **UI primitives** are style-only and do not know about game content.

### Content as data

All game-specific information lives in `src/content/` as typed records. Components receive IDs or arrays of these records. This makes it easy to add, remove, or hide entries without restructuring pages.

Example content types:

```ts
export interface GameMechanic {
  id: string;
  title: string;
  description: string;      // approved copy only
  media?: MediaRef;
  isPublished: boolean;
}

export interface MediaItem {
  id: string;
  type: 'image' | 'video';
  src: string;
  alt: string;              // required, factual alt text
  caption?: string;         // approved caption
  credit?: string;
  category?: 'screenshot' | 'environment' | 'character' | 'artwork';
}

export interface DevelopmentPost {
  id: string;
  date: string;             // ISO 8601
  title: string;
  summary: string;
  body?: string;            // approved markdown or plain text
  media?: MediaRef[];
  isPublished: boolean;
}
```

Unapproved entries must have `isPublished: false` and must not be rendered in production builds.

## 3. Styling and design guidelines

### Visual identity

The site should feel like an invitation into OakHaven's world. Maintain a consistent, restrained visual system so that artwork remains the focus.

- **Mood:** cinematic forest survival — deep woods, mist, firelight, weathered materials.
- **Color palette:**
  - Background: charcoal / deep pine (`#0f1113`, `#141a17`)
  - Surface: dark moss / bark (`#1e2421`, `#2a302c`)
  - Text primary: warm off-white (`#f4f1ea`)
  - Text secondary: mist / sage (`#9ca99d`)
  - Accent: ember / amber (`#d4a574`) used sparingly for calls to action and highlights
  - Caution: avoid bright gradients, SaaS-style blues, or neon accents that clash with the game's grounded palette.
- **Typography:**
  - Display: one expressive serif or stylized sans for major titles.
  - Body: a highly readable sans-serif for paragraphs and UI.
  - Scale: use a tight type scale. Avoid oversized marketing headlines that compete with artwork.
- **Texture:** subtle grain or paper texture is allowed, but keep it low-contrast and non-repeating. No heavy vignettes over readable areas.
- **Borders and rules:** thin, low-opacity rules (`border-white/10`) to separate sections without visual weight.

### Layout principles

- Use generous vertical spacing. Let images breathe.
- Favor full-bleed cinematic sections over boxed card grids.
- Alternate text/image placement only when it improves the story; do not force alternation as a template.
- Keep the maximum content width constrained (`max-w-7xl` or similar) so text does not stretch across ultra-wide screens.
- Avoid generic three-up feature cards unless the content genuinely calls for equal comparison.

### Image treatment

- Do not add artificial filters, color overlays, or heavy effects that alter approved artwork.
- Text overlays must sit over dark or low-detail image regions, or use a scrim no stronger than `bg-black/40`.
- Captions and credits must be visible and unobtrusive.

## 4. Responsive design requirements

- Build mobile-first. All layouts must be usable at `320px` width.
- **Hero:** use a mobile-appropriate crop. The desktop hero may hide the subject on a phone; use `picture` + `srcset` or a responsive background when art direction is needed.
- **Navigation:**
  - Desktop: horizontal links, compact header.
  - Mobile: a single menu button that opens a clear overlay or sheet. Large tap targets (min `44×44px`). Close on Escape, trap focus, and restore focus on close.
- **Typography:** use `clamp()` or Tailwind's responsive prefix scale so headings do not overflow small screens.
- **Media grid:** one column on mobile; expand to two or an editorial asymmetric grid on larger screens.
- **Spacing:** reduce section padding on mobile (`py-16` → `py-24` → `py-32`).
- **Touch:** no hover-only interactions. All actions must be reachable by tap.
- **Zoom:** ensure the layout remains usable at 200% zoom and does not break at 1280px+ widths.
- **Orientation:** verify landscape phone layouts, especially for the hero and gallery lightbox.

## 5. Asset management conventions

### File locations

```text
public/
└── assets/
    ├── brand/              # logo, wordmark, favicon
    ├── hero/               # homepage hero images and posters
    ├── gameplay/           # gameplay screenshots and captures
    ├── environments/       # environment art
    ├── characters/         # character art and renders
    ├── gallery/            # general gallery images
    └── video/              # poster images and video files (if hosted)
```

### Naming

- Use lowercase kebab-case with descriptive prefixes:
  - `oakhaven-hero-forest-2400w.avif`
  - `gameplay-crafting-workstation-1600w.webp`
- Include a width suffix when multiple sizes exist.
- Do not use generic names like `image1.jpg` or `screenshot.png`.

### Image requirements

- Prefer AVIF with WebP fallback; JPEG only for compatibility.
- Provide multiple widths (`400w`, `800w`, `1200w`, `1600w`, `2400w`) for important images.
- Always include `width` and `height` attributes to prevent layout shift.
- Use lazy loading for below-the-fold images. Hero and first visible image may be eagerly loaded.
- Maximum total decode budget: no single page should load more than ~2.5 MB of images on initial load.
- Do not include unrelated stock photography, AI-generated placeholders, or concept art not approved for the game.

### Video requirements

- Do not autoplay video with sound.
- Use a static poster image as the default state.
- Provide a visible play/pause control.
- Prefer streaming or external hosting for large files; do not commit large video files to the repository without approval.

### Content metadata

Every media asset in the content manifest must include:

- `alt`: required, factual description of what the image shows.
- `caption`: optional approved caption.
- `credit`: optional artist or source credit.
- `category`: optional grouping for filters.

### Placeholder policy

- Use a single, consistent placeholder component (`ContentPlaceholder`) for sections awaiting approved content.
- Placeholders are for internal development only. Do not render placeholder copy or gray boxes on the public site.
- If a section has no published entries, do not publish the section. Hide it via the content manifest, not with CSS.

## 6. Animation guidelines

Motion should support atmosphere and discovery. It must never block access to content.

### Allowed animations

- Fade and slide-in for sections entering the viewport (opacity + transform only).
- Subtle parallax on hero background layers, respecting `prefers-reduced-motion`.
- Button/link hover states: opacity, scale, or underline transitions under `200ms`.
- Gallery lightbox open/close transition.
- Mobile menu slide/fade transition under `300ms`.

### Forbidden patterns

- Full-screen intro sequences that must complete before interaction.
- Scroll-jacking or hijacked momentum scrolling.
- Multi-step staggered animations that delay the appearance of primary content.
- Non-essential animation on every element.
- Animations that cannot be paused or disabled.

### Reduced motion

- Read `prefers-reduced-motion` at the top of the app and disable nonessential motion globally.
- Convert movement animations to simple opacity fades or remove them entirely.
- Keep focus outlines and instant state changes functional.

### Performance

- Animate only `transform` and `opacity`.
- Avoid animating `box-shadow`, `blur`, `width`, `height`, `top`, `left`, or `margin`.
- Remove animations from off-screen elements; do not run continuous loops on hidden sections.

## 7. Performance requirements

- **Target:** Lighthouse Performance score ≥ 90 on mobile and desktop for a representative page.
- **First Contentful Paint:** ≤ 1.8s on 4G.
- **Largest Contentful Paint:** ≤ 2.5s.
- **Cumulative Layout Shift:** ≤ 0.1.
- **Total blocking time:** minimize long JavaScript tasks; code-split routes if the bundle grows.
- **Images:** responsive sizes, modern formats, lazy loading, explicit dimensions.
- **Fonts:** preload only the display font; use `font-display: swap`; subset if possible.
- **Third-party scripts:** avoid them. Analytics or newsletter embeds require explicit approval.
- **Build output:** enable Vite build optimizations; review the bundle with `npm run preview` and browser devtools.

## 8. Accessibility requirements

- **Structure:** use semantic HTML (`header`, `main`, `nav`, `section`, `article`, `footer`).
- **Landmarks:** one `main` per page; `nav` for primary navigation.
- **Skip link:** a visible "Skip to main content" link at the top of every page.
- **Headings:** logical hierarchy (`h1` once per page, no skipped levels).
- **Focus:** visible focus indicators on all interactive elements; focus management for menus, modals, and lightbox.
- **Images:** every image must have meaningful `alt` text. Decorative images use `alt=""`.
- **Color contrast:** minimum 4.5:1 for body text, 3:1 for large text and UI components.
- **Text over images:** ensure contrast holds across the image area; use scrims or adjacent text blocks when needed.
- **Motion:** honor `prefers-reduced-motion`.
- **Video:** provide captions or transcripts for meaningful video content.
- **Keyboard:** full keyboard navigation; gallery supports arrow keys and Escape; mobile menu supports Escape and Tab trapping.
- **Testing:** run automated checks with Lighthouse and axe-core; perform a manual keyboard-only pass.

## 9. Build and testing commands

Install dependencies from the `Website` directory:

```bash
cd OakHaven/Website
npm install
```

### Development

```bash
npm run dev
```

Starts the Vite dev server. Open the local URL shown in the terminal.

### Build

```bash
npm run build
```

Produces a production build in `dist/`. Review warnings and errors; do not ignore TypeScript or build failures.

### Preview production build

```bash
npm run preview
```

Serves the built site locally. Use this to verify performance, routing, and asset loading before any deployment discussion.

### Linting and type checking

```bash
npm run lint
npm run typecheck
```

If these scripts are not yet configured in `package.json`, add them using `eslint` and `tsc --noEmit`. Keep the project lint-clean.

### Testing

- **Manual:** navigate every route with mouse, keyboard, and a mobile viewport.
- **Accessibility:** run Lighthouse and axe DevTools on each page.
- **Performance:** run Lighthouse in incognito with throttling; inspect network waterfall.
- **Cross-browser:** verify in the latest Chrome, Firefox, Safari, and Edge if possible.

## 10. Rules for maintaining consistency across website sections

### Content consistency

- The only source of game truth is `src/content/`. Do not hard-code game descriptions, character names, location names, or mechanics into components.
- Every public-facing claim must be traceable to an approved content entry.
- Use `isPublished: false` to hide incomplete entries. Never render placeholder lore to fill space.

### Visual consistency

- Use Tailwind theme tokens and custom CSS variables for colors, spacing, and typography. Do not invent ad-hoc values in components.
- Keep border radius, shadows, and transitions consistent with the design system.
- Reuse `CinematicHero`, `ImageFeature`, `ContentSection`, and `PageIntro` rather than rebuilding section layouts.
- Maintain the same header, footer, and navigation on every page.

### Component consistency

- If a section pattern appears more than once, extract it to `components/content/`.
- Keep page components thin: they should import sections, pass content, and little else.
- Do not duplicate media handling logic; always go through `ResponsiveImage` and `VideoFigure`.

### Navigation consistency

- The primary navigation should remain the same across pages, with the current page indicated by an `aria-current="page"` attribute.
- Footer links should mirror official destinations from `site.ts`.
- Do not add one-off navigation links inside page bodies.

### Quality gates

Before any change is considered complete:

1. No TypeScript errors.
2. No lint errors.
3. No broken image references.
4. No placeholder content visible in production builds.
5. All interactive elements keyboard-accessible.
6. Color contrast passes for new text/image combinations.
7. Responsive layout verified at 320px, 768px, and 1440px.
8. `prefers-reduced-motion` tested.

## Absolute project rules

- **Never invent game content.** Do not create fake lore, characters, locations, or mechanics.
- **Never substitute actual game screenshots with unrelated imagery.** If an asset is missing, leave a clearly marked placeholder in development and hide the section from the public build.
- **Maintain a consistent visual identity.** Use the palette, typography, spacing, and component patterns defined here.
- **Avoid unnecessary dependencies.** Add a new dependency only when the standard stack cannot reasonably solve the problem.
- **Keep components reusable and maintainable.** Prefer composition over inheritance; avoid mega-components.
- **Avoid unnecessarily complicated architectures.** Do not add state managers, CMS adapters, or backend services unless explicitly required.
- **Do not modify files outside this website project.** The website lives in `OakHaven/Website`. Do not touch Unity assets, scripts, or project configuration in the parent repository.
