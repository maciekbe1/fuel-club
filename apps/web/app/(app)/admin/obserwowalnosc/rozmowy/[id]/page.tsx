import Link from "next/link";
import { Suspense } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { ConversationTraceView } from "@/features/admin-observability/components/ConversationTraceView";

export default function ConversationPage({
  params,
}: PageProps<"/admin/obserwowalnosc/rozmowy/[id]">) {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/admin/obserwowalnosc/rozmowy"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Rozmowy
        </Link>
        <h1 className="text-2xl font-semibold">Rozmowa</h1>
      </div>
      <Suspense fallback={<Skeleton className="h-64 rounded-2xl" />}>
        <Trace params={params} />
      </Suspense>
    </div>
  );
}

async function Trace({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <ConversationTraceView key={id} id={id} />;
}
