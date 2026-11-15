export interface GameplayMechanic {
  id: string;
  title: string;
  description: string;
}

export const gameplayOverview = {
  heading: 'Gameplay Mechanics',
  subheading:
    'OakHaven blends co-op survival, exploration, and investigation. These are the design pillars that shape how players interact with the world and each other.',
};

export const gameplayMechanics: GameplayMechanic[] = [
  {
    id: 'co-op-survival',
    title: 'Co-op Survival',
    description:
      'Players work together to explore the village, gather resources, craft supplies, and make it through each day and night. Survival depends on coordination and careful resource management.',
  },
  {
    id: 'day-night',
    title: 'Day and Night',
    description:
      'The village changes after dark. Day is the safer time to scavenge and plan; night brings greater danger, but also the chance for greater rewards for those willing to risk it.',
  },
  {
    id: 'inventory',
    title: 'Weight-Based Inventory',
    description:
      'Inventory space is managed by weight rather than slots. Players must decide what to carry, what to leave behind, and when to return to shelter before they are overburdened.',
  },
  {
    id: 'resource-degradation',
    title: 'Resource Degradation',
    description:
      'Resources do not last forever. Food spoils and supplies degrade over time, pushing players to keep moving, keep exploring, and avoid hoarding.',
  },
  {
    id: 'shelter',
    title: 'Shelter and Talismans',
    description:
      'Closed buildings can be turned into safe refuges by hanging a talisman inside. Talismans require maintenance and resources to stay effective, making shelter management a constant concern.',
  },
  {
    id: 'investigation',
    title: 'Investigation',
    description:
      'Clues scattered across the village help the group piece together the truth behind their situation. Paying attention to details and sharing information are key to making progress.',
  },
  {
    id: 'group-decisions',
    title: 'Group Decisions',
    description:
      'On a fixed schedule, the group can vote to isolate one member for questioning while the others continue exploring. These decisions shape strategy and survival.',
  },
  {
    id: 'upgrades',
    title: 'Personal and Team Upgrades',
    description:
      'Players can improve their own capabilities and contribute to team-wide upgrades. Balancing personal growth with group benefits is part of the long-term strategy.',
  },
];

export interface GameplayVideo {
  src: string;
  poster?: string;
  caption?: string;
}

export const gameplayVideo: GameplayVideo = {
  src: '/assets/gameplay/gameplay-overview.mp4',
  caption: 'OakHaven gameplay overview — exploration, scavenging, and co-op survival in the village.',
};
