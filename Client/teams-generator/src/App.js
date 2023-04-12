import logo from './logo.svg';
import './App.css';
import PlayersTable from './Components/PlayersTable';
import getPlayersDEMO from './Adapters/DB/playersDB';
import MainMenu from './Components/MainMenu';
import { TeamsContextProvider } from './Store/TeamsContext';
import { ConfigurationContextProvider } from './Store/ConfigurationContext';
import MyCard from './Components/UI/MyCard';
import { Card, ConfigProvider } from 'antd';
import { PlayersContextProvider } from './Store/PlayersContext';
import { BrowserView, MobileView } from 'react-device-detect';
import MobileMainScreen from './Components/Mobile/MobileMainScreen';

function App() {
  const players = getPlayersDEMO()
  return (
    <ConfigurationContextProvider>
      <TeamsContextProvider>
        <PlayersContextProvider>
          <Card style={{ marginTop: '4%', marginRight: '10%', marginLeft: '10%' }}>
          <MainMenu />
          </Card>
        </PlayersContextProvider>
      </TeamsContextProvider>
    </ConfigurationContextProvider>
  )
}

export default App;
