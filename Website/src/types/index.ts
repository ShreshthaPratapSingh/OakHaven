export interface NavLink {
  label: string;
  href: string;
}

export interface SiteMetadata {
  title: string;
  tagline: string;
  description: string;
}

export interface HeroContent {
  title: string;
  subtitle: string;
  primaryCta: {
    label: string;
    href: string;
  };
  secondaryCta: {
    label: string;
    href: string;
  };
}

export interface GameIntroductionContent {
  heading: string;
  paragraphs: string[];
  imageAlt: string;
}

export interface MediaItem {
  id: string;
  type: 'image' | 'video';
  src: string;
  alt: string;
  caption?: string;
  credit?: string;
  category?: 'screenshot' | 'environment' | 'character' | 'artwork';
  width?: number;
  height?: number;
  srcSet?: string;
  sizes?: string;
}

export interface DevelopmentPost {
  id: string;
  date: string;
  title: string;
  summary: string;
  body?: string;
  isPublished: boolean;
}
