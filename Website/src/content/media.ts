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
  // Approved media assets will be added here with src, alt, caption, category, width, and height.
];
