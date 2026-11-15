import { cn } from '@/lib/utils';
import { Children, cloneElement, isValidElement } from 'react';
import type { ButtonHTMLAttributes, ReactElement, ReactNode } from 'react';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  variant?: 'primary' | 'secondary' | 'ghost';
  asChild?: boolean;
}

export function Button({
  children,
  variant = 'primary',
  className,
  asChild = false,
  ...props
}: ButtonProps) {
  const variants = {
    primary:
      'bg-ember text-pine-950 hover:bg-ember/90 focus:ring-ember/50',
    secondary:
      'bg-white/10 text-paper border border-white/20 hover:bg-white/15 focus:ring-white/30',
    ghost:
      'bg-transparent text-paper hover:bg-white/5 focus:ring-white/20',
  };

  const classes = cn(
    'inline-flex items-center justify-center rounded-sm px-6 py-3 text-sm font-medium tracking-wide transition-all duration-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-pine-900 disabled:opacity-50',
    variants[variant],
    className
  );

  if (asChild) {
    const child = Children.only(children);
    if (!isValidElement(child)) {
      throw new Error('Button with asChild expects a single valid React element as child.');
    }

    return cloneElement(child as ReactElement<{ className?: string }>, {
      className: cn(classes, (child.props as { className?: string }).className),
      ...props,
    });
  }

  return (
    <button className={classes} {...props}>
      {children}
    </button>
  );
}
