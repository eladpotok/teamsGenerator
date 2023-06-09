import './App.css';
import getPlayersDEMO from './Adapters/DB/playersDB';
import MainMenu from './Components/MainMenu';
import { TeamsContextProvider } from './Store/TeamsContext';
import { ConfigurationContext, ConfigurationContextProvider } from './Store/ConfigurationContext';
import { Card } from 'antd';
import { PlayersContextProvider } from './Store/PlayersContext';
import { BrowserView, MobileView } from 'react-device-detect';
import { ConfigurationStoreContext, ConfigurationStoreContextProvider } from './Store/ConfigurationStoreContext';
import MobileMainScreen from './Components/Mobile/MobileMainScreen';
import { useContext, useEffect } from 'react';
import { getInitialConfig } from './Adapters/DB/WebApiAdapter';
import Main from './Components/Main';
import {  logEvent } from "firebase/analytics";
import { AnalyticsEventManager } from './Adapters/AnalyticsEventSender';
import { AnalyticsContextProvider } from './Store/AnalyticsContext';
import { UserContext, UserContextProvider } from './Store/UserContext';
import { isMobile } from "react-device-detect";



function App() {

  const players = getPlayersDEMO()

  const className = isMobile ? 'app-mobile' : 'app-web'
  return (
    <div className={className}  >
    <UserContextProvider>
      <ConfigurationStoreContextProvider>
        <ConfigurationContextProvider>
          <TeamsContextProvider>
            <PlayersContextProvider>
              <AnalyticsContextProvider>
                <Main />
              </AnalyticsContextProvider>
            </PlayersContextProvider>
          </TeamsContextProvider>
        </ConfigurationContextProvider>
      </ConfigurationStoreContextProvider>
    </UserContextProvider>
    </div>
  )
}

export default App;
