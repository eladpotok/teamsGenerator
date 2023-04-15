import { useContext, useEffect, useState } from "react";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import { ConfigurationStoreContext } from "../Store/ConfigurationStoreContext";
import { getInitialConfig, getTeams } from "../Adapters/FakeWebApiAdapter";
import dayjs from 'dayjs';
import { BrowserView, MobileView } from "react-device-detect";
import { Card, message } from "antd";
import MainMenu from "./MainMenu";
import MobileMainScreen from "./Mobile/MobileMainScreen";
import { PlayersContext } from "../Store/PlayersContext";
import { TeamsContext } from "../Store/TeamsContext";

function Main(props) {

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

                configContext.setUserConfig({
                    eventDate: dayjs(new Date()),
                    shirtsColors: initialConfig.config.shirtsColors.slice(0, 3),
                    numberOfTeams: initialConfig.config.numberOfTeams,
                    algo: initialConfig.algos.filter(t => t.algoKey == 0)[0]
                })
            }
        })()
    }, [storeConfigContext.storeConfig])

    function algoSelectChangedHandler(value) {
        configContext.setUserConfig({ ...configContext.userConfig, algo: value })
    };

    function resetHandler() {
        playersContext.setPlayers([])
    }

    async function generateTeamsHandler() {

        if (configContext.userConfig.length < 3) {
            messageApi.open({
                type: 'error',
                content: 'Must choose 3 shirts colors at least',
              });
            return false;
        }
        
        if (playersContext.players.length < 5) {
            messageApi.open({
                type: 'error',
                content: 'Must have 5 players at least',
              });
            return false;
        }

        const getTeamsResponse = await getTeams(playersContext.players, configContext.userConfig)
        teamsContext.setTeams(getTeamsResponse.teams)
        return true
    }
    

    return (
        <>
            {contextHolder}
            <BrowserView>
                <Card style={{ marginTop: '4%', marginRight: '10%', marginLeft: '10%' }}>
                    <MainMenu />
                </Card>
            </BrowserView>
            <MobileView>
                {storeConfigContext.storeConfig && configContext.userConfig && <MobileMainScreen  onGenerateTeams={generateTeamsHandler} onResetClicked={resetHandler} onAlgoChanged={algoSelectChangedHandler} storeConfig={storeConfigContext.storeConfig} teams={teamsContext.teams}/>}
            </MobileView>
        </>
    )

}

export default Main;