export interface CharacterEntry {
  id: string;
  name: string;
  roleDescription: string;
  abilityHint: string;
  imageAlt: string;
  isRevealed: boolean;
}

export const charactersOverview = {
  heading: 'Characters',
  subheading:
    'OakHaven follows a group of friends trapped together. Each one brings their own strengths to the struggle for survival.',
};

export const charactersPublicInfo = {
  title: 'A Group of Friends',
  description:
    'The survivors of OakHaven must rely on each other to gather resources, secure shelter, and understand the force that holds them in the village.',
  abilities:
    'Every character has access to shared survival abilities and one unique ability that sets them apart. Ability details will be revealed as development continues.',
};

export const characterEntries: CharacterEntry[] = [
  // Placeholder entries for approved character reveals.
  // No character names, appearances, or backstories have been approved for public release.
];

export const charactersComingSoon = {
  title: 'More Characters Revealed Soon',
  description:
    'Names, appearances, backstories, and unique abilities will be shared once they are approved for release.',
};
