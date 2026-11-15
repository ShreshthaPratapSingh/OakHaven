import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { MechanicCard } from '@/components/content/MechanicCard';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import { gameplayOverview, gameplayMechanics } from '@/content/gameplay';

export function GameplayPage() {
  return (
    <>
      <PageIntro
        heading={gameplayOverview.heading}
        subheading={gameplayOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <div className="mb-12 md:mb-16">
            <PlaceholderImage
              label="Gameplay capture — awaiting approved asset"
              aspectRatio="video"
              className="rounded-sm shadow-2xl"
            />
          </div>

          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {gameplayMechanics.map((mechanic, index) => (
              <MechanicCard
                key={mechanic.id}
                title={mechanic.title}
                description={mechanic.description}
                index={index}
              />
            ))}
          </div>
        </div>
      </ContentSection>
    </>
  );
}
