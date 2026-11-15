import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { FadeIn } from '@/components/content/FadeIn';
import { PlaceholderImage } from '@/components/media/PlaceholderImage';
import {
  charactersOverview,
  charactersPublicInfo,
  charactersComingSoon,
} from '@/content/characters';

export function CharactersPage() {
  return (
    <>
      <PageIntro
        heading={charactersOverview.heading}
        subheading={charactersOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <div className="grid items-center gap-12 lg:grid-cols-2 lg:gap-16">
            <FadeIn>
              <div className="space-y-6">
                <h2 className="text-balance text-3xl font-semibold tracking-tight text-paper md:text-4xl">
                  {charactersPublicInfo.title}
                </h2>
                <p className="text-base leading-relaxed text-mist md:text-lg">
                  {charactersPublicInfo.description}
                </p>
                <p className="text-base leading-relaxed text-mist md:text-lg">
                  {charactersPublicInfo.abilities}
                </p>
              </div>
            </FadeIn>

            <FadeIn delay={150}>
              <PlaceholderImage
                label="Character artwork — awaiting approved asset"
                aspectRatio="video"
                className="rounded-sm shadow-2xl"
              />
            </FadeIn>
          </div>
        </div>
      </ContentSection>

      <ContentSection>
        <div className="mx-auto max-w-3xl px-6 text-center lg:px-8">
          <FadeIn>
            <h2 className="text-balance text-2xl font-semibold tracking-tight text-paper md:text-3xl">
              {charactersComingSoon.title}
            </h2>
            <p className="mt-4 text-base leading-relaxed text-mist md:text-lg">
              {charactersComingSoon.description}
            </p>
          </FadeIn>
        </div>
      </ContentSection>
    </>
  );
}
