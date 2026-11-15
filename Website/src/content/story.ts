export interface StoryEntry {
  id: string;
  title: string;
  description: string;
  imageLabel: string;
  imageAlt: string;
  src?: string;
}

export const storyOverview = {
  heading: 'Story & Lore',
  subheading:
    'A group of friends is trapped in a village they cannot leave. Survival is only the beginning—understanding what holds them there is the real mystery.',
};

export const storyEntries: StoryEntry[] = [
  {
    id: 'the-premise',
    title: 'The Premise',
    description:
      'OakHaven begins with a group of friends cut off from the outside world. To escape, they must survive long enough to learn the rules of the place and confront whatever keeps them there.',
    imageLabel: 'Story scene — awaiting approved asset',
    imageAlt: 'A scene from OakHaven establishing the trapped village.',
    src: '/assets/environments/story-premise.webp',
  },
  {
    id: 'the-investigation',
    title: 'The Investigation',
    description:
      'Clues are scattered throughout the village and its surroundings. Notes, objects, and environmental details hint at the truth behind the trap. Players must pay attention and piece together fragments to make progress.',
    imageLabel: 'Investigation scene — awaiting approved asset',
    imageAlt: 'A clue or investigation moment in OakHaven.',
    src: '/assets/gameplay/story-investigation.webp',
  },
  {
    id: 'the-threat',
    title: 'The Threat',
    description:
      'Something controls the village. It watches, it waits, and it turns the environment itself against the group. Understanding its nature is the only path to freedom.',
    imageLabel: 'Threat scene — awaiting approved asset',
    imageAlt: 'An atmospheric scene hinting at the force within OakHaven.',
    src: '/assets/environments/story-threat.webp',
  },
];

export const storyComingSoon = {
  title: 'More to Come',
  description:
    'Character backstories, location histories, and deeper lore will be shared as development continues and details are approved for release.',
};
