import { ContentSection } from './ContentSection';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import type { GameIntroductionContent } from '@/types';

interface GameIntroductionProps {
  content: GameIntroductionContent;
}

export function GameIntroduction({ content }: GameIntroductionProps) {
  return (
    <ContentSection className="bg-pine-800/30">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="grid items-center gap-12 lg:grid-cols-2 lg:gap-16">
          <div>
            <h2 className="text-balance text-3xl font-semibold tracking-tight text-paper md:text-4xl">
              {content.heading}
            </h2>
            <div className="mt-6 space-y-4">
              {content.paragraphs.map((paragraph, index) => (
                <p
                  key={index}
                  className="text-base leading-relaxed text-mist md:text-lg"
                >
                  {paragraph}
                </p>
              ))}
            </div>
          </div>

          <div className="order-first lg:order-last">
            <PlaceholderImage
              label="Gameplay screenshot — awaiting approved asset"
              aspectRatio="video"
              className="rounded-sm shadow-2xl"
            />
          </div>
        </div>
      </div>
    </ContentSection>
  );
}
