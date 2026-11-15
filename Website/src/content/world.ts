import type { MediaItem } from '@/types';

export interface EnvironmentEntry {
  id: string;
  title: string;
  description: string;
  imageLabel: string;
  imageAlt: string;
}

export const worldOverview = {
  heading: 'The World',
  subheading:
    'Trapped in an inescapable village, survival depends on reading the land, securing shelter, and understanding how the environment itself turns against you.',
};

export const environmentEntries: EnvironmentEntry[] = [
  {
    id: 'the-village',
    title: 'The Village',
    description:
      'OakHaven takes place in a village with no easy way out. Players must explore carefully, scavenge what they need, and learn the rhythm of the place before night falls.',
    imageLabel: 'Village environment — awaiting approved asset',
    imageAlt: 'A wide view of the village environment in OakHaven.',
  },
  {
    id: 'day-and-night',
    title: 'Day and Night',
    description:
      'The village changes after dark. Day is the safer time to scavenge and plan; night brings greater danger, but also the chance for greater rewards for those willing to risk it.',
    imageLabel: 'Day and night cycle — awaiting approved asset',
    imageAlt: 'The village transitioning between day and night.',
  },
  {
    id: 'shelter',
    title: 'Shelter',
    description:
      'Closed buildings can become refuges when a talisman is hung inside. Talismans require maintenance and resources to stay effective, making shelter management a constant concern.',
    imageLabel: 'Shelter interior — awaiting approved asset',
    imageAlt: 'A secured building interior used as shelter.',
  },
  {
    id: 'scavenging',
    title: 'Scavenging',
    description:
      'Resources, clues, and valuable items are scattered throughout the area. Players must decide how far to explore, how much to carry, and when to return before the environment turns hostile.',
    imageLabel: 'Scavenging environment — awaiting approved asset',
    imageAlt: 'A scavenging location within the village.',
  },
  {
    id: 'corruption',
    title: 'The Growing Corruption',
    description:
      'As the nights pass, a corruption spreads through the village. The longer the group survives, the more oppressive the world becomes.',
    imageLabel: 'Corrupted environment — awaiting approved asset',
    imageAlt: 'A corrupted area of the village.',
  },
];

export const worldMedia: MediaItem[] = [
  // Placeholder for approved world/environment screenshots.
];
