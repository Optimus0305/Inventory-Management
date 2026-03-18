import { InventoryDashboard } from './components/InventoryDashboard';
import { CreateHoldForm } from './components/CreateHoldForm';
import { ActiveHoldsList } from './components/ActiveHoldsList';
import { useInventory } from './hooks/useInventory';
import { useHolds } from './hooks/useHolds';
import './App.css';

function App() {
  const { items, loading, error, refresh } = useInventory();
  const { holds, addHold, updateHold } = useHolds();

  return (
    <div className="app">
      <header className="app-header">
        <h1>🏭 Inventory Hold Manager</h1>
        <p className="subtitle">Place and manage temporary holds on inventory items</p>
      </header>

      <main className="app-main">
        <InventoryDashboard
          items={items}
          loading={loading}
          error={error}
          onRefresh={refresh}
        />

        <CreateHoldForm
          inventory={items}
          onHoldCreated={addHold}
          onInventoryRefresh={refresh}
        />

        <ActiveHoldsList
          holds={holds}
          onHoldUpdated={updateHold}
          onInventoryRefresh={refresh}
        />
      </main>
    </div>
  );
}

export default App;
