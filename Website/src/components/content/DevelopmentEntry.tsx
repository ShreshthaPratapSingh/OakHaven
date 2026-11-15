import { FadeIn } from './FadeIn';

interface DevelopmentEntryProps {
  title: string;
  status?: string;
  description: string;
  index?: number;
  isHighlighted?: boolean;
}

export function DevelopmentEntry({
  title,
  status,
  description,
  index = 0,
  isHighlighted = false,
}: DevelopmentEntryProps) {
  return (
    <FadeIn delay={index * 100}>
      <article
        className={`relative rounded-sm border p-6 md:p-8 ${
          isHighlighted
            ? 'border-ember/30 bg-pine-800/50'
            : 'border-white/10 bg-pine-800/30'
        }`}
      >
        {isHighlighted && (
          <span className="absolute -top-3 left-6 inline-block rounded-sm bg-ember px-3 py-1 text-xs font-semibold uppercase tracking-wider text-pine-950">
            Current
          </span>
        )}

        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <h3 className="text-balance text-xl font-semibold tracking-tight text-paper md:text-2xl">
              {title}
            </h3>
            {status && (
              <p className="mt-1 text-sm font-medium text-ember">{status}</p>
            )}
          </div>
        </div>

        <p className="mt-4 text-base leading-relaxed text-mist">{description}</p>
      </article>
    </FadeIn>
  );
}
