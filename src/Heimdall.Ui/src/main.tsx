import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { BrowserRouter } from "react-router-dom"
import "@fontsource-variable/geist/index.css"
import "@fontsource-variable/geist-mono/index.css"
import "./index.css"
import App from "./App.tsx"

// The console is served from the gateway's /_apim namespace, so the router lives under that basename.
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter basename="/_apim">
      <App />
    </BrowserRouter>
  </StrictMode>,
)
