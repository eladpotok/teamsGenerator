import { useContext, useEffect } from "react";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import { ConfigurationStoreContext } from "../Store/ConfigurationStoreContext";
import { getInitialConfig, getTeams } from "../Adapters/DB/WebApiAdapter";
import dayjs from 'dayjs';
import { message } from "antd";
import MobileMainScreen from "./Mobile/MobileMainScreen";
import { PlayersContext } from "../Store/PlayersContext";
import { TeamsContext } from "../Store/TeamsContext";
import {Dimensions} from 'react-native'; 
import { AnalyticsContext } from "../Store/AnalyticsContext";
import { UserContext } from "../Store/UserContext";
import { takeNElementsFromDic } from "../Utilities/Helpers";
import { isMobile } from "react-device-detect";

function Main(props) {
    
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)
    const configContext = useContext(ConfigurationContext);
    const storeConfigContext = useContext(ConfigurationStoreContext);
    const playersContext = useContext(PlayersContext)
    const teamsContext = useContext(TeamsContext)

    const [messageApi, contextHolder] = message.useMessage();

    
    useEffect(() => {
        (async () => {
            if (!storeConfigContext.storeConfig) {
                let initialConfig = await getInitialConfig();
                storeConfigContext.setStoreConfig(initialConfig)

                const defaultConfig = {
                    eventDate: dayjs(new Date()),
                    shirtsColors: takeNElementsFromDic(initialConfig.config.shirtsColors, 3),
                    numberOfTeams: initialConfig.config.numberOfTeams,
                    algo: initialConfig.algos.filter(t => t.algoKey === 0)[0]
                }
                configContext.prepareConfig(defaultConfig)
            }

        })()
    }, [storeConfigContext.storeConfig])

    useEffect(() => {
        ( () => {
            if (userContext.user) {
                analyticsContext.sendAnalyticsImpression(userContext.user.uid, 'mainApp')        
            }
        })()
    }, userContext.user)

    function algoSelectChangedHandler(value) {
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'algoChanged', value.algoName)
        configContext.setUserConfig({ ...configContext.userConfig, algo: value })
    };

    function resetHandler() {
        playersContext.setPlayers([])
        teamsContext.setTeams(null)
    }

    function removeAllPlayersHandler(){
        playersContext.setPlayers([])
    }

    function removePlayerHandler(playerToRemove){
        const updatedPlayers = playersContext.players.filter(player => player.key !== playerToRemove.key)
        playersContext.setPlayers(updatedPlayers)
    }

    function removePlayerFromTeamHandler(fromTeam, player) {
        teamsContext.removePlayer(fromTeam, player)
    }
    
    function movePlayerHandler(fromTeam, toTeam, player) {
        teamsContext.movePlayer(fromTeam, toTeam, player)
    }

    function shirtColorChangedHandler(teamIdFrom, newColor){
        const newColorShirt =
        teamsContext.changeShirtColor(teamIdFrom, newColor)
    }

    async function generateTeamsHandler() {

        const playersToGenerate = playersContext.arrivedPlayers
        if (configContext.userConfig.length < 3) {
            messageApi.open({
                type: 'error',
                content: 'Must choose 3 shirts colors at least',
              });
            return false;
        }
        
        if (playersToGenerate.length < 5) {
            messageApi.open({
                type: 'error',
                content: 'Must have 5 players at least',
              });
            return false;
        }

        const getTeamsResponse = await getTeams(playersToGenerate, configContext.userConfig)
        teamsContext.setTeams(getTeamsResponse.teams)

        analyticsContext.sendAnalyticsImpression(userContext.user.uid, 'generated-teams-view')
        analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'generatedTeams', {
            playersCount: playersContext.arrivedPlayers.length,
            shirtsCount: configContext.userConfig.shirtsColors.length
        })


        return true
    }
    
    // RN version:0.57.0
    let deviceH = Dimensions.get('screen').height;
    // the value returned does not include the bottom navigation bar, I am not sure why yours does.
    let windowH = Dimensions.get('window').height;
    let bottomNavBarH = deviceH - windowH;


    const divStyle = isMobile ? {} : {width: '75%'}
    
//style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'center'  }}
    return (
        <div style={divStyle}>
            {contextHolder}
            {/* <BrowserView>
                <Card style={{ marginTop: '4%', marginRight: '10%', marginLeft: '10%' }}>
                    <MainMenu />
                </Card>
            </BrowserView>
            <MobileView> */}
                {storeConfigContext.storeConfig && configContext.userConfig && <MobileMainScreen screenHeight={bottomNavBarH} onChangeShirtColor={shirtColorChangedHandler} onClearPlayers={removeAllPlayersHandler} onMovePlayer={movePlayerHandler} onRemovePlayerFromTeam={removePlayerFromTeamHandler} onRemovePlayer={removePlayerHandler} onGenerateTeams={generateTeamsHandler} onResetClicked={resetHandler} onAlgoChanged={algoSelectChangedHandler} storeConfig={storeConfigContext.storeConfig} teams={teamsContext.teams}/>}
            {/* </MobileView> */}
        </div>
    )

}

export default Main;