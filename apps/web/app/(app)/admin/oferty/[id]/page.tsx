import Link from "next/link";
import { Suspense } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { EditRewardForm } from "@/features/admin-rewards/components/EditRewardForm";

export default function EditOfferPage({
  params,
}: PageProps<"/admin/oferty/[id]">) {
  return (
    <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/admin/oferty"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Oferty
        </Link>
        <h1 className="text-2xl font-semibold">Edycja oferty</h1>
      </div>
      <Suspense fallback={<Skeleton className="h-96 rounded-3xl" />}>
        <Editor params={params} />
      </Suspense>
    </div>
  );
}

async function Editor({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <EditRewardForm key={id} id={id} />;
}
