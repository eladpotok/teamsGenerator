import { useContext } from "react";
import { ConfigurationContext } from "../../Store/ConfigurationContext";
import MobilePlayersMenu from "./Players/MobilePlayersMenu";
import MobileHeader from "./MobileHeader";
import MobileFooter from "./MobileFooter";
import './MobileMainScreen.css'
import { PlayersContext } from "../../Store/PlayersContext";
import MobilePlayersListPanel from "./Players/MobilePlayersListPanel";
import { AnalyticsContext } from "../../Store/AnalyticsContext";

function MobileMainScreen(props) {

    const configContext = useContext(ConfigurationContext);
    const playersContext = useContext(PlayersContext)
    const analyticsContext = useContext(AnalyticsContext)


    function algoChangedHandler(value) {
        props.onAlgoChanged(value)

        analyticsContext.sendContentEvent('algo_changed')
    };

    function playerArrivedHandler(player, value){
        playersContext.setPlayerArrived(player, value)
    }

    function markAllPlayersHandler(value) {
        playersContext.setAllPlayerArrived(value)
    }


    return (

        <body className="box">
            <header style={{ marginLeft: '4px', marginRight: '4px' }}>
                {configContext.userConfig.algo && <MobileHeader onClearPlayers={props.onClearPlayers} players={playersContext.players} storeConfig={props.storeConfig} userConfig={configContext.userConfig} onAlgoChanged={algoChangedHandler} defaultAlgoName={configContext.userConfig.algo.displayName} />}
            </header>
            <header>
                <MobilePlayersListPanel onMarkAllPlayers={markAllPlayersHandler} onClearPlayers={props.onClearPlayers} onRemovePlayer={props.onRemovePlayer} algos={props.storeConfig.algos} arrivedPlayers={playersContext.arrivedPlayers} currentAlgo={configContext.userConfig.algo} players={playersContext.players}/>
            </header>
            <main className="row">
                {configContext.userConfig.algo && <MobilePlayersMenu onPlayerArrived={playerArrivedHandler}  onRemovePlayer={props.onRemovePlayer} players={playersContext.players} currentAlgo={configContext.userConfig.algo} />}
            </main>
            <footer style={{ marginLeft: '4px', marginRight: '4px' }}>
                <MobileFooter shirtsColors={configContext.userConfig.shirtsColors} onChangeShirtColor={props.onChangeShirtColor} onResetClicked={props.onResetClicked} onGenerateTeams={props.onGenerateTeams} onMovePlayer={props.onMovePlayer} onRemovePlayerFromTeam={props.onRemovePlayerFromTeam} teams={props.teams} />
            </footer>
        </body>
    )
}


export default MobileMainScreen;