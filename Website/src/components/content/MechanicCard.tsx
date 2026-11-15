import { FadeIn } from './FadeIn';

interface MechanicCardProps {
  title: string;
  description: string;
  index: number;
}

export function MechanicCard({ title, description, index }: MechanicCardProps) {
  const formattedIndex = String(index + 1).padStart(2, '0');

  return (
    <FadeIn delay={Math.min(index * 50, 250)}>
      <article className="group relative h-full rounded-sm border border-white/10 bg-pine-800/30 p-6 transition-colors hover:border-ember/30 hover:bg-pine-800/50 md:p-8">
        <span
          aria-hidden="true"
          className="mb-4 block font-display text-3xl font-bold text-ember/30 transition-colors group-hover:text-ember/50"
        >
          {formattedIndex}
        </span>
        <h3 className="text-balance text-xl font-semibold tracking-tight text-paper">
          {title}
        </h3>
        <p className="mt-3 text-sm leading-relaxed text-mist md:text-base">
          {description}
        </p>
      </article>
    </FadeIn>
  );
}
