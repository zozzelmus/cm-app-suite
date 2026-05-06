import { Routes, Route } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { HomePage } from '@/routes/HomePage'
import { CasesPage } from '@/routes/CasesPage'
import { IntakePage } from '@/routes/IntakePage'
import { SignedOutPage } from '@/routes/SignedOutPage'

export default function App() {
  return (
    <Routes>
      <Route path="/signed-out" element={<SignedOutPage />} />
      <Route path="/" element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="cases" element={<CasesPage />} />
        <Route path="intake" element={<IntakePage />} />
      </Route>
    </Routes>
  )
}
