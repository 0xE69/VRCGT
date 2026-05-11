export default function SettingsPage() {
  return (
    <div className="flex-1 overflow-y-auto">
      <div className="max-w-3xl mx-auto flex flex-col gap-6">
        <div>
          <h1 className="text-xl font-bold text-white">Settings</h1>
          <p className="text-sm text-gray-400 mt-1">Application and account settings.</p>
        </div>

        <section className="bg-gray-900 border border-dashed border-gray-700 rounded-xl p-8 text-center text-sm text-gray-600">
          Settings panels — Phase 2
        </section>
      </div>
    </div>
  )
}
