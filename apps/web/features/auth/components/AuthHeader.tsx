export function AuthHeader({ title, description }: { title: string; description: string }) {
  return (
    <header className="flex flex-col gap-1">
      <h2 className="text-2xl font-bold tracking-tight">{title}</h2>
      <p className="text-muted-foreground">{description}</p>
    </header>
  );
}
