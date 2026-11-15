import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { ImageFeature } from '@/components/content/ImageFeature';
import { FadeIn } from '@/components/content/FadeIn';
import { storyOverview, storyEntries, storyComingSoon } from '@/content/story';

export function StoryPage() {
  return (
    <>
      <PageIntro
        heading={storyOverview.heading}
        subheading={storyOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-7xl px-6 lg:px-8">
          <div className="flex flex-col gap-16 md:gap-24 lg:gap-32">
            {storyEntries.map((entry, index) => (
              <ImageFeature
                key={entry.id}
                title={entry.title}
                description={entry.description}
                imageLabel={entry.imageLabel}
                imageAlt={entry.imageAlt}
                reversed={index % 2 === 1}
              />
            ))}
          </div>
        </div>
      </ContentSection>

      <ContentSection>
        <div className="mx-auto max-w-3xl px-6 text-center lg:px-8">
          <FadeIn>
            <h2 className="text-balance text-2xl font-semibold tracking-tight text-paper md:text-3xl">
              {storyComingSoon.title}
            </h2>
            <p className="mt-4 text-base leading-relaxed text-mist md:text-lg">
              {storyComingSoon.description}
            </p>
          </FadeIn>
        </div>
      </ContentSection>
    </>
  );
}
