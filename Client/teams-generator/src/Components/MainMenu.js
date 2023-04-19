import { useContext, useEffect, useState } from "react";
import { getInitialConfig, getTeams } from '../Adapters/DB/WebApiAdapter'
import { Button, Card,  Modal, Tabs,Tooltip,message } from 'antd';
import PlayersTable from "./PlayersTable";
import Teams from "./Teams/Teams";
import { TeamsContext } from "../Store/TeamsContext";
import Config from "./Configuration/Config";
import { QuestionCircleOutlined, QuestionCircleTwoTone } from "@ant-design/icons";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import { PlayersContext } from "../Store/PlayersContext";
import { ConfigurationStoreContext } from "../Store/ConfigurationStoreContext";

function MainMenu(props) {

    const teamsContext = useContext(TeamsContext);
    const configContext = useContext(ConfigurationContext);
    const storeConfigContext = useContext(ConfigurationStoreContext);
    const playersContext = useContext(PlayersContext);

    const [messageApi, contextHolder] = message.useMessage();
    const [teamsModalOpened, setTeamsModalOpened] = useState(false)

    async function generateTeamsHandler() {
        if (configContext.userConfig.length < 3) {
            messageApi.open({
                type: 'error',
                content: 'Must choose 3 shirts colors at least',
              });
            return;
        }
        const getTeamsResponse = await getTeams(playersContext.players, configContext.userConfig)
        teamsContext.setTeams(getTeamsResponse.teams)
        setTeamsModalOpened(true)
    }

    function tabChangedHandler(e) {
        configContext.setUserConfig({...configContext.userConfig, algo: e})
    }

    useEffect(() => {
        (async () => {
            if (!storeConfigContext.storeConfig) {
                let initialConfig = await getInitialConfig();
                storeConfigContext.setStoreConfig(initialConfig)
            }
        })()
    }, [storeConfigContext.storeConfig])

    return (
        <div>
            {contextHolder}

            <div>
                <div style={{ display: 'flex', justifyContent: 'flex-start', flexDirection: 'row' }}>
                    {storeConfigContext.storeConfig && <Config currentAlgo={storeConfigContext.storeConfig.algos.filter(t=>t.algoKey === 0)[0]} shirtsColors={storeConfigContext.storeConfig.config.shirtsColors} numberOfTeams={storeConfigContext.storeConfig.config.numberOfTeams}/>}
                    
                    <Card style={{ height: '100%', flex: 1, marginLeft: '4px' }} title="Algorithms" >
                        <QuestionCircleOutlined />  <label style={{ color: "gray", marginLeft: '4px' }}>Here are the most common algorithms for football teams creation. Choose your algorithm and add the players</label>
                        {storeConfigContext.storeConfig && <Tabs  onChange={tabChangedHandler} style={{ margin: '10px' }} items={getTabsToShow(storeConfigContext.storeConfig.algos, generateTeamsHandler)} defaultActiveKey={0}/>}
                    </Card>
                </div>
            </div>

            <Modal onCancel={() => { setTeamsModalOpened(false) }} width={1000} open={teamsModalOpened} footer={[]}>
                {teamsContext.teams && <Teams teams={teamsContext.teams} onShuffleClicked={generateTeamsHandler} />}
            </Modal>

            <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>

                <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-start', 'justifyContent': 'flex-start' }}>

                { teamsContext.teams &&   <Button  onClick={()=>{setTeamsModalOpened(true)}} type="primary" style={{ borderRadius: '5px', marginTop: '5px', marginRight: '10px' }}>
                        Show Last Results
                    </Button>}
                    {playersContext.players.length < 5 && <Tooltip title='Available only when 5 players added'>
                        <QuestionCircleTwoTone  style={{marginTop: '10px', marginRight: '5px', fontSize: '24px'}} />
                    </Tooltip>}
                    <Button disabled={playersContext.players.length < 5} onClick={generateTeamsHandler} type="primary" style={{ borderRadius: '5px', marginTop: '5px' }}>
                            Run!
                    </Button>
                </div>

            </div>
        </div>
    )

}




function getTabsToShow(algos, generateTeamsHandler) {
    return algos && algos.map((algo) => {
        return {
            label: algo.displayName,
            key: algo.algoKey,
            children: <PlayersTable onGenerateClicked={generateTeamsHandler} style={{ height: '100px' }} algoKey={algo.algoKey} />
        }
    })
}


export default MainMenu;