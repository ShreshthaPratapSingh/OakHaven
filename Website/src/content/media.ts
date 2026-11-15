import type { MediaItem } from '@/types';

export const mediaOverview = {
  heading: 'Media',
  subheading:
    'Screenshots, artwork, and videos from OakHaven. New assets will be added as they are approved for release.',
};

export const mediaCategories = [
  'all',
  'screenshot',
  'environment',
  'character',
  'artwork',
] as const;

export type MediaCategory = (typeof mediaCategories)[number];

export const mediaItems: MediaItem[] = [
  {
    id: 'screenshot-crafting',
    type: 'image',
    src: '/assets/gallery/screenshot-crafting.webp',
    alt: 'Crafting and survival tools at the workbench',
    caption: 'Crafting gear and managing supplies to prepare for nightfall.',
    category: 'screenshot',
    width: 1920,
    height: 1080,
  },
  {
    id: 'screenshot-exploration',
    type: 'image',
    src: '/assets/gallery/screenshot-exploration.webp',
    alt: 'Exploring the outer edges of the village',
    caption: 'Scavenging for rare materials across the settlement.',
    category: 'screenshot',
    width: 1920,
    height: 1080,
  },
  {
    id: 'environment-village',
    type: 'image',
    src: '/assets/gallery/environment-village.webp',
    alt: 'The mist-shrouded village of OakHaven',
    caption: 'Mist hangs low between the weathered wooden buildings.',
    category: 'environment',
    width: 1920,
    height: 1080,
  },
  {
    id: 'character-survivor',
    type: 'image',
    src: '/assets/gallery/character-survivor.webp',
    alt: 'Survivor venturing out before nightfall',
    caption: 'A survivor braves the woods before the safe hours expire.',
    category: 'character',
    width: 1920,
    height: 1080,
  },
  {
    id: 'artwork-concept',
    type: 'image',
    src: '/assets/gallery/artwork-concept.webp',
    alt: 'OakHaven atmospheric concept art',
    caption: 'Concept artwork capturing the secluded forest settlement.',
    category: 'artwork',
    width: 1920,
    height: 1080,
  },
  {
    id: 'teaser-trailer',
    type: 'video',
    src: '/assets/video/teaser-trailer.mp4',
    alt: 'OakHaven Official Gameplay Teaser',
    caption: 'First look at the atmosphere and survival gameplay.',
    category: 'screenshot',
  },
];
