import Files from 'react-files'
import { PlayersContext } from '../../Store/PlayersContext';
import { useContext } from 'react';
import { message } from 'antd';
import { ConfigurationContext } from '../../Store/ConfigurationContext';
import { AnalyticsContext } from '../../Store/AnalyticsContext';
import { UserContext } from '../../Store/UserContext';



function ImportPlayer(props) {
    const [messageApi, contextHolder] = message.useMessage();
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)

    const playersContext = useContext(PlayersContext);
    const configContext = useContext(ConfigurationContext)

    function readFileHandler(files) {
        const key = 'updatable';

        const fileReader = new FileReader();

        fileReader.onload = e => {
            const fileData = JSON.parse(e.target.result)
            const {players, algoKey } = fileData

            playersContext.setPlayers(players)

            const selectedAlgo = props.algos.filter(algo => algo.algoKey == algoKey)[0]
            configContext.setUserConfig({ ...configContext.userConfig, algo: selectedAlgo })

            messageApi.open({
                key,
                type: 'success',
                content: 'Loaded!',
                duration: 2,
            })

            analyticsContext.sendAnalyticsEngagement(userContext.user.uid, 'importPlayers', null)

        };
        messageApi.open({
            key,
            type: 'loading',
            content: 'Loading...',
        })
        fileReader.readAsText(files[0], "UTF-8");
    }

    return (<Files
        className='files-dropzone'
        onChange={readFileHandler}
        accepts={['.json']}
        maxFileSize={10000000}
        minFileSize={0}
        clickable>
        <>
        {contextHolder}
        {props.children}
        </>
    </Files>)
}

export default ImportPlayer