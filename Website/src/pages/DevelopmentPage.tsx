import { PageIntro } from '@/components/content/PageIntro';
import { ContentSection } from '@/components/content/ContentSection';
import { DevelopmentEntry } from '@/components/content/DevelopmentEntry';
import { FadeIn } from '@/components/content/FadeIn';
import {
  developmentOverview,
  currentStatus,
  developmentComingSoon,
} from '@/content/development';

export function DevelopmentPage() {
  return (
    <>
      <PageIntro
        heading={developmentOverview.heading}
        subheading={developmentOverview.subheading}
      />

      <ContentSection className="bg-pine-800/30">
        <div className="mx-auto max-w-4xl px-6 lg:px-8">
          <DevelopmentEntry
            title={currentStatus.title}
            status={currentStatus.status}
            description={currentStatus.description}
            isHighlighted
          />
        </div>
      </ContentSection>

      <ContentSection>
        <div className="mx-auto max-w-3xl px-6 text-center lg:px-8">
          <FadeIn>
            <h2 className="text-balance text-2xl font-semibold tracking-tight text-paper md:text-3xl">
              {developmentComingSoon.title}
            </h2>
            <p className="mt-4 text-base leading-relaxed text-mist md:text-lg">
              {developmentComingSoon.description}
            </p>
          </FadeIn>
        </div>
      </ContentSection>
    </>
  );
}
