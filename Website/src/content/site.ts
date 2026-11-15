import type { NavLink, SiteMetadata, HeroContent } from '@/types';

export const siteMetadata: SiteMetadata = {
  title: 'OakHaven',
  tagline: 'A co-op survival horror experience.',
  description:
    'OakHaven is a co-op survival horror game for PC. Survive day and night in an inescapable village, scavenge for resources, secure shelter, and uncover the truth hidden in the world around you.',
};

export const navLinks: NavLink[] = [
  { label: 'The Game', href: '/game' },
  { label: 'Gameplay', href: '/gameplay' },
  { label: 'The World', href: '/world' },
  { label: 'Story', href: '/story' },
  { label: 'Characters', href: '/characters' },
  { label: 'Media', href: '/media' },
  { label: 'Development', href: '/development' },
];

export const heroContent: HeroContent = {
  title: 'OakHaven',
  subtitle: 'A co-op survival horror experience.',
  backgroundImage: '/assets/hero/hero-bg.webp',
  primaryCta: {
    label: 'Explore the Game',
    href: '/game',
  },
  secondaryCta: {
    label: 'View Media',
    href: '/media',
  },
};
