import { CinematicHero } from '@/components/content/CinematicHero';
import { GameIntroduction } from '@/components/content/GameIntroduction';
import { heroContent } from '@/content/site';
import { gameIntroduction } from '@/content/game';

export function HomePage() {
  return (
    <>
      <CinematicHero content={heroContent} />
      <GameIntroduction content={gameIntroduction} />
    </>
  );
}
