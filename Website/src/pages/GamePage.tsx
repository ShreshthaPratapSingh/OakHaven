import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { ResponsiveImage } from '@/components/media/ResponsiveImage';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import { gameIntroduction } from '@/content/game';

const gameplayActionImage = {
  src: '/assets/gameplay/gameplay-action.webp',
  alt: 'Gameplay action capture showing exploration in OakHaven',
};

export function GamePage() {
  return (
    <>
      <PageIntro
        heading="The Game"
        subheading="Explore how OakHaven plays, from gathering and crafting to surviving alongside other players."
      />

      <ContentSection>
        <div className="mx-auto max-w-3xl px-6 lg:px-8">
          <div className="space-y-6">
            {gameIntroduction.paragraphs.map((paragraph, index) => (
              <p
                key={index}
                className="text-base leading-relaxed text-mist md:text-lg"
              >
                {paragraph}
              </p>
            ))}
          </div>
        </div>
      </ContentSection>

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <h2 className="text-balance text-2xl font-semibold tracking-tight text-paper md:text-3xl">
            Gameplay in Action
          </h2>
          <p className="mt-4 max-w-2xl text-mist">
            More gameplay media will be added as approved captures become
            available.
          </p>
          <div className="mt-10">
            {gameplayActionImage.src ? (
              <ResponsiveImage
                src={gameplayActionImage.src}
                alt={gameplayActionImage.alt}
                aspectRatio="video"
                className="rounded-sm shadow-2xl"
              />
            ) : (
              <PlaceholderImage
                label="Gameplay capture — awaiting approved asset"
                aspectRatio="video"
                className="rounded-sm shadow-2xl"
              />
            )}
          </div>
        </div>
      </ContentSection>
    </>
  );
}
