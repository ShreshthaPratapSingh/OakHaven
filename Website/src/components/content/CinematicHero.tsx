import { Link } from 'react-router-dom';
import { ChevronDown } from 'lucide-react';
import { Button } from '@/components/ui/Button';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import { useReducedMotion } from '@/hooks/useReducedMotion';
import type { HeroContent } from '@/types';

interface CinematicHeroProps {
  content: HeroContent;
}

export function CinematicHero({ content }: CinematicHeroProps) {
  const reducedMotion = useReducedMotion();

  return (
    <section
      aria-label="Hero"
      className="relative flex min-h-screen items-center justify-center overflow-hidden"
    >
      <div className="absolute inset-0">
        <PlaceholderImage
          label="Hero artwork — awaiting approved asset"
          aspectRatio="wide"
          className="h-full w-full"
        />
        <div className="absolute inset-0 bg-gradient-to-t from-pine-900 via-pine-900/60 to-pine-900/30" />
      </div>

      <div
        className={`relative z-10 mx-auto max-w-5xl px-6 pt-20 text-center lg:px-8 ${
          reducedMotion ? '' : 'animate-fade-in-up'
        }`}
      >
        <p className="mb-4 text-sm font-medium uppercase tracking-[0.2em] text-ember">
          Official Website
        </p>
        <h1 className="text-balance font-display text-6xl font-bold tracking-tight text-paper sm:text-7xl md:text-8xl lg:text-9xl">
          {content.title}
        </h1>
        <p className="mx-auto mt-6 max-w-2xl text-balance text-lg leading-relaxed text-mist md:text-xl">
          {content.subtitle}
        </p>

        <div className="mt-10 flex flex-col items-center justify-center gap-4 sm:flex-row">
          <Button asChild>
            <Link to={content.primaryCta.href}>{content.primaryCta.label}</Link>
          </Button>
          <Button variant="secondary" asChild>
            <Link to={content.secondaryCta.href}>{content.secondaryCta.label}</Link>
          </Button>
        </div>
      </div>

      <div className="absolute bottom-8 left-1/2 z-10 -translate-x-1/2 animate-bounce text-mist/60">
        <ChevronDown className="h-6 w-6" aria-hidden="true" />
        <span className="sr-only">Scroll down</span>
      </div>
    </section>
  );
}
